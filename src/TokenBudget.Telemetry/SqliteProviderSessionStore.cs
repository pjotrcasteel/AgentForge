using System.Text.Json;
using Microsoft.Data.Sqlite;
using TokenBudget.Billing;
using TokenBudget.Domain;

namespace TokenBudget.Telemetry;

/// <summary>
/// Account-scoped cumulative-session ledger. Separate from legacy Codex run ingestion;
/// it never combines assistant.usage per-call events with session.usage.getMetrics totals.
/// </summary>
public sealed class SqliteProviderSessionStore
{
    private const int MinimumCalibrationSamples = 20;
    private static readonly JsonSerializerOptions Serialization = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    /// <summary>Open a local metrics-only ledger without recording prompts, secrets or transcripts.</summary>
    public SqliteProviderSessionStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string path = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            // Each operation owns its connection; pooled handles prevent safe cleanup of local ledgers on Windows.
            Pooling = false
        };
        _connectionString = builder.ToString();
    }

    /// <summary>Insert or advance one cumulative session checkpoint transactionally; duplicates carry no usage delta.</summary>
    public async Task<ProviderSessionIngestResult> IngestAsync(ProviderSessionCheckpoint incoming,
        CancellationToken cancellationToken = default)
    {
        ProviderSessionCheckpoint next = NormalizeAndValidate(incoming);
        ActualUsageObservation observation = next.Observation;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        ProviderSessionCheckpoint? prior = await ReadAsync(connection, transaction, observation.Account,
            observation.SessionId, cancellationToken);

        if (prior is not null)
        {
            next = Reconcile(prior, next);
        }

        ProviderUsageDelta delta = Delta(prior?.Observation, next.Observation);
        bool changedCounters = delta.InputTokens != 0 || delta.OutputTokens != 0 ||
            delta.AiCredits is > 0 || delta.PremiumRequests is > 0;
        bool changedMetadata = prior is null || prior.Completed != next.Completed ||
            prior.Match != next.Match || !EqualsModels(prior.Observation.Models, next.Observation.Models) ||
            prior.Observation.CreditsVerified != next.Observation.CreditsVerified;
        ProviderCheckpointChange change = prior is null ? ProviderCheckpointChange.Inserted :
            prior.Completed == false && next.Completed ? ProviderCheckpointChange.Finalized :
            changedCounters ? ProviderCheckpointChange.Advanced :
            changedMetadata ? ProviderCheckpointChange.MetadataUpdated : ProviderCheckpointChange.Duplicate;

        if (change != ProviderCheckpointChange.Duplicate)
        {
            await using var save = connection.CreateCommand();
            save.Transaction = transaction;
            save.CommandText = """
                INSERT INTO provider_session_checkpoints(provider, account_id, session_id, checkpoint_json)
                VALUES ($provider, $account, $session, $content)
                ON CONFLICT(provider, account_id, session_id)
                DO UPDATE SET checkpoint_json = excluded.checkpoint_json;
                """;
            save.Parameters.AddWithValue("$provider", observation.Account.Provider.ToString());
            save.Parameters.AddWithValue("$account", observation.Account.AccountId);
            save.Parameters.AddWithValue("$session", observation.SessionId);
            save.Parameters.AddWithValue("$content", JsonSerializer.Serialize(next, Serialization));
            await save.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        bool matched = IsEligibleForTokens(next);
        bool matchedCost = matched && IsEligibleForCost(next);
        return new ProviderSessionIngestResult(change, delta, matched, matchedCost);
    }

    /// <summary>Retrieve one exact account/session checkpoint; other providers and accounts cannot share its key.</summary>
    public async Task<ProviderSessionCheckpoint?> GetAsync(ProviderAccount account, string sessionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(account, sessionId);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return await ReadAsync(connection, null, account, sessionId, cancellationToken);
    }

    /// <summary>Summarize only exact single-model, completed matches; at least 20 runs are needed before calibration.</summary>
    public async Task<ProviderSessionHistorySummary> SummarizeAsync(ProviderAccount account, string modelId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(account, "summary");
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 128)
        {
            throw new ArgumentException("Model identifier is required (1..128 characters).", nameof(modelId));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT checkpoint_json FROM provider_session_checkpoints
            WHERE provider = $provider AND account_id = $account;
            """;
        query.Parameters.AddWithValue("$provider", account.Provider.ToString());
        query.Parameters.AddWithValue("$account", account.AccountId);
        var tokenRatios = new List<double>();
        int sessions = 0;
        int complete = 0;
        int covered = 0;
        int verifiedCosts = 0;
        int coveredCosts = 0;

        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ProviderSessionCheckpoint checkpoint = Deserialize(reader.GetString(0));
            if (checkpoint.Observation.Models is not [var soleModel] ||
                !string.Equals(soleModel, modelId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sessions++;
            if (!checkpoint.Completed)
            {
                continue;
            }

            complete++;
            if (!IsEligibleForTokens(checkpoint))
            {
                continue;
            }

            long actual = checkpoint.Observation.Usage.TotalTokens;
            TokenRange projected = checkpoint.Match!.ForecastTokens;
            tokenRatios.Add((double)actual / projected.P50);
            if (actual <= projected.P90)
            {
                covered++;
            }

            if (IsEligibleForCost(checkpoint))
            {
                verifiedCosts++;
                if (checkpoint.Observation.AiCredits <= checkpoint.Match.ForecastCosts!.P90)
                {
                    coveredCosts++;
                }
            }
        }

        tokenRatios.Sort();
        int matched = tokenRatios.Count;
        double? median = matched == 0 ? null : matched % 2 == 1
            ? tokenRatios[matched / 2] : (tokenRatios[matched / 2 - 1] + tokenRatios[matched / 2]) / 2;
        bool adequate = matched >= MinimumCalibrationSamples;
        return new ProviderSessionHistorySummary(account, modelId, sessions, complete, matched, verifiedCosts,
            median, matched == 0 ? null : (double)covered / matched,
            verifiedCosts == 0 ? null : (double)coveredCosts / verifiedCosts, adequate,
            adequate ? "Matched usage evidence exists; forecast calibration remains manual and disabled." :
                $"Only {matched} comparable completed runs; {MinimumCalibrationSamples} required to consider calibration.");
    }

    /// <summary>
    /// Evaluate account/model-specific historical coverage with conservative confidence bounds;
    /// no stored forecast, budget decision or authorization is modified.
    /// </summary>
    public async Task<ProviderCalibrationDiagnostics> AnalyzeCalibrationAsync(ProviderAccount account, string modelId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(account, "calibration");
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 128)
        {
            throw new ArgumentException("Model identifier is required (1..128 characters).", nameof(modelId));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT checkpoint_json FROM provider_session_checkpoints
            WHERE provider = $provider AND account_id = $account;
            """;
        query.Parameters.AddWithValue("$provider", account.Provider.ToString());
        query.Parameters.AddWithValue("$account", account.AccountId);

        var checkpoints = new List<ProviderSessionCheckpoint>();
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            checkpoints.Add(Deserialize(reader.GetString(0)));
        }

        return ProviderCalibrationAnalyzer.Evaluate(account, modelId, checkpoints);
    }

    private static ProviderSessionCheckpoint NormalizeAndValidate(ProviderSessionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ActualUsageObservation observation = checkpoint.Observation ??
            throw new ArgumentException("Usage observation is required.", nameof(checkpoint));
        ValidateIdentity(observation.Account, observation.SessionId);
        ArgumentNullException.ThrowIfNull(observation.Usage);
        if (!observation.IsCumulative)
        {
            throw new ArgumentException("Only cumulative session metrics may enter the ledger; per-call events must not be added.");
        }

        UsageBreakdown usage = observation.Usage;
        if (usage.InputTokens < 0 || usage.OutputTokens < 0 || usage.CachedInputTokens is < 0 ||
            usage.CacheWriteInputTokens is < 0 || usage.ReasoningOutputTokens is < 0 ||
            usage.CachedInputTokens > usage.InputTokens || usage.CacheWriteInputTokens > usage.InputTokens ||
            usage.ReasoningOutputTokens > usage.OutputTokens || observation.AiCredits is < 0 ||
            observation.PremiumRequests is < 0 || observation.ObservedAt == default ||
            string.IsNullOrWhiteSpace(observation.Source) || observation.Source.Length > 256 ||
            observation.Source.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid cumulative counters, source label or timestamp.");
        }

        string[] models = (observation.Models ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase).ToArray();
        if (models.Length > 16 || models.Any(model => string.IsNullOrWhiteSpace(model) || model.Length > 128))
        {
            throw new ArgumentException("Session model identities must be bounded and nonempty.");
        }

        observation = observation with { Models = models };
        if (checkpoint.Match is { } match)
        {
            if (!checkpoint.Completed || match.EstimateId == Guid.Empty || match.Provider != observation.Account.Provider ||
                models.Length != 1 || !string.Equals(models[0], match.ModelId, StringComparison.OrdinalIgnoreCase) ||
                match.ForecastTokens.P50 <= 0 || match.ForecastTokens.P90 < match.ForecastTokens.P50 ||
                match.ForecastTokens.P10 < 0 || match.ForecastTokens.P10 > match.ForecastTokens.P50 ||
                (match.ForecastCosts is not null && (match.CostUnit is null || match.ForecastCosts.P50 <= 0 ||
                    match.ForecastCosts.P90 < match.ForecastCosts.P50 || match.ForecastCosts.P10 < 0)))
            {
                throw new ArgumentException("A forecast match requires a completed single-model session and compatible estimate.");
            }
        }

        return checkpoint with { Observation = observation };
    }

    private static ProviderSessionCheckpoint Reconcile(ProviderSessionCheckpoint previous, ProviderSessionCheckpoint incoming)
    {
        ActualUsageObservation old = previous.Observation;
        ActualUsageObservation current = incoming.Observation;
        if (!string.Equals(old.Source, current.Source, StringComparison.Ordinal) ||
            (previous.Completed && !incoming.Completed) ||
            (previous.Completed && (old.Usage.InputTokens != current.Usage.InputTokens ||
                old.Usage.OutputTokens != current.Usage.OutputTokens)) ||
            (incoming.Match is not null && previous.Match is not null && previous.Match != incoming.Match))
        {
            throw new InvalidOperationException("Session identity, completed totals or attached forecast changed.");
        }

        if (current.Usage.InputTokens < old.Usage.InputTokens || current.Usage.OutputTokens < old.Usage.OutputTokens ||
            (old.AiCredits.HasValue && current.AiCredits.HasValue && current.AiCredits < old.AiCredits) ||
            (old.PremiumRequests.HasValue && current.PremiumRequests.HasValue && current.PremiumRequests < old.PremiumRequests) ||
            (old.Usage.CachedInputTokens.HasValue && current.Usage.CachedInputTokens.HasValue &&
                current.Usage.CachedInputTokens < old.Usage.CachedInputTokens) ||
            (old.Usage.CacheWriteInputTokens.HasValue && current.Usage.CacheWriteInputTokens.HasValue &&
                current.Usage.CacheWriteInputTokens < old.Usage.CacheWriteInputTokens) ||
            (old.Usage.ReasoningOutputTokens.HasValue && current.Usage.ReasoningOutputTokens.HasValue &&
                current.Usage.ReasoningOutputTokens < old.Usage.ReasoningOutputTokens) ||
            (current.ObservedAt < old.ObservedAt && (current.Usage.InputTokens != old.Usage.InputTokens ||
                current.Usage.OutputTokens != old.Usage.OutputTokens)))
        {
            throw new InvalidOperationException("Out-of-order or regressing cumulative session counters.");
        }

        if (old.Models is { } priorModels && priorModels.Except(current.Models ?? [], StringComparer.OrdinalIgnoreCase).Any())
        {
            throw new InvalidOperationException("A cumulative session cannot forget a previously observed model.");
        }

        UsageBreakdown currentUsage = current.Usage with
        {
            CachedInputTokens = current.Usage.CachedInputTokens ?? old.Usage.CachedInputTokens,
            CacheWriteInputTokens = current.Usage.CacheWriteInputTokens ?? old.Usage.CacheWriteInputTokens,
            ReasoningOutputTokens = current.Usage.ReasoningOutputTokens ?? old.Usage.ReasoningOutputTokens
        };
        bool unchangedTokens = old.Usage.InputTokens == currentUsage.InputTokens &&
            old.Usage.OutputTokens == currentUsage.OutputTokens;
        bool retainedVerifiedCredits = old.CreditsVerified && unchangedTokens &&
            (current.AiCredits is null || current.AiCredits == old.AiCredits);
        current = current with
        {
            Usage = currentUsage,
            AiCredits = current.AiCredits ?? old.AiCredits,
            PremiumRequests = current.PremiumRequests ?? old.PremiumRequests,
            ObservedAt = current.ObservedAt >= old.ObservedAt ? current.ObservedAt : old.ObservedAt,
            CreditsVerified = current.CreditsVerified && current.AiCredits.HasValue || retainedVerifiedCredits
        };
        return incoming with { Observation = current, Match = incoming.Match ?? previous.Match };
    }

    private static ProviderUsageDelta Delta(ActualUsageObservation? old, ActualUsageObservation current) => new(
        current.Usage.InputTokens - (old?.Usage.InputTokens ?? 0),
        current.Usage.OutputTokens - (old?.Usage.OutputTokens ?? 0),
        current.AiCredits.HasValue ? current.AiCredits - (old?.AiCredits ?? 0) : null,
        current.PremiumRequests.HasValue ? current.PremiumRequests - (old?.PremiumRequests ?? 0) : null);

    private static bool EqualsModels(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
        (left ?? []).SequenceEqual(right ?? [], StringComparer.OrdinalIgnoreCase);

    private static bool IsEligibleForTokens(ProviderSessionCheckpoint checkpoint) =>
        checkpoint.Completed && checkpoint.Match is not null && checkpoint.Observation.Models is [var model] &&
        string.Equals(model, checkpoint.Match.ModelId, StringComparison.OrdinalIgnoreCase);

    private static bool IsEligibleForCost(ProviderSessionCheckpoint checkpoint) =>
        checkpoint.Observation.CreditsVerified && checkpoint.Observation.AiCredits.HasValue &&
        checkpoint.Observation.Account.Provider == BillingProvider.GitHubCopilot &&
        checkpoint.Match?.CostUnit == BudgetUnit.GitHubAiCredits && checkpoint.Match.ForecastCosts is not null;

    private static void ValidateIdentity(ProviderAccount account, string sessionId)
    {
        if (account is null || string.IsNullOrWhiteSpace(account.AccountId) || account.AccountId.Length > 128 ||
            string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256)
        {
            throw new ArgumentException("Provider account and session identifiers are required and bounded.");
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS provider_session_checkpoints (
                provider TEXT NOT NULL, account_id TEXT NOT NULL, session_id TEXT NOT NULL,
                checkpoint_json TEXT NOT NULL,
                PRIMARY KEY(provider, account_id, session_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ProviderSessionCheckpoint?> ReadAsync(SqliteConnection connection,
        SqliteTransaction? transaction, ProviderAccount account, string sessionId, CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT checkpoint_json FROM provider_session_checkpoints
            WHERE provider = $provider AND account_id = $account AND session_id = $session;
            """;
        query.Parameters.AddWithValue("$provider", account.Provider.ToString());
        query.Parameters.AddWithValue("$account", account.AccountId);
        query.Parameters.AddWithValue("$session", sessionId);
        object? value = await query.ExecuteScalarAsync(cancellationToken);
        return value is string json ? Deserialize(json) : null;
    }

    private static ProviderSessionCheckpoint Deserialize(string json) =>
        JsonSerializer.Deserialize<ProviderSessionCheckpoint>(json, Serialization) ??
        throw new InvalidDataException("The provider session ledger contains an invalid checkpoint.");
}
