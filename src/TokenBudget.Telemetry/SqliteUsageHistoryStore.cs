using Microsoft.Data.Sqlite;

namespace TokenBudget.Telemetry;

/// <summary>Idempotent, parameterized local storage for usage counters and forecast comparisons.</summary>
public sealed class SqliteUsageHistoryStore
{
    private const int MinimumCalibrationSamples = 20;
    private readonly string _connectionString;

    public SqliteUsageHistoryStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("SQLite database path must be specified.", nameof(databasePath));
        }

        string fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            // Do not retain a file handle after a metrics-only operation completes (notably on Windows).
            Pooling = false
        };
        _connectionString = builder.ToString();
    }

    public async Task<IngestResult> IngestAsync(CodexRunObservation run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Usage is { } usage && (usage.InputTokens < 0 || usage.OutputTokens < 0 ||
            usage.CachedInputTokens > usage.InputTokens || usage.ReasoningOutputTokens > usage.OutputTokens))
        {
            throw new ArgumentException("Usage counters contain inconsistent subsets.", nameof(run));
        }

        if (run.Complete && (run.Usage is null || run.CompletedTurns == 0 || run.FailedTurns > 0))
        {
            throw new ArgumentException("Complete runs require valid counters and no failed turns.", nameof(run));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await InitializeAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.Transaction = (SqliteTransaction)transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO codex_runs (
                run_id, fingerprint, profile, model_id, thread_id, complete, completed_turns, failed_turns,
                input_tokens, output_tokens, cached_input_tokens, reasoning_output_tokens,
                estimate_id, forecast_p50, forecast_p90, recorded_utc)
            VALUES ($run, $fingerprint, $profile, $model, $thread, $complete, $turns, $failures,
                $input, $output, $cached, $reasoning, $estimate, $p50, $p90, $timestamp);
            """;
        insert.Parameters.AddWithValue("$run", run.RunId);
        insert.Parameters.AddWithValue("$fingerprint", run.Fingerprint);
        insert.Parameters.AddWithValue("$profile", run.Profile);
        insert.Parameters.AddWithValue("$model", run.ModelId);
        insert.Parameters.AddWithValue("$thread", (object?)run.ThreadId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$complete", run.Complete ? 1 : 0);
        insert.Parameters.AddWithValue("$turns", run.CompletedTurns);
        insert.Parameters.AddWithValue("$failures", run.FailedTurns);
        insert.Parameters.AddWithValue("$input", (object?)run.Usage?.InputTokens ?? DBNull.Value);
        insert.Parameters.AddWithValue("$output", (object?)run.Usage?.OutputTokens ?? DBNull.Value);
        insert.Parameters.AddWithValue("$cached", (object?)run.Usage?.CachedInputTokens ?? DBNull.Value);
        insert.Parameters.AddWithValue("$reasoning", (object?)run.Usage?.ReasoningOutputTokens ?? DBNull.Value);
        insert.Parameters.AddWithValue("$estimate", (object?)run.EstimateId?.ToString("D") ?? DBNull.Value);
        insert.Parameters.AddWithValue("$p50", (object?)run.ForecastP50 ?? DBNull.Value);
        insert.Parameters.AddWithValue("$p90", (object?)run.ForecastP90 ?? DBNull.Value);
        insert.Parameters.AddWithValue("$timestamp", DateTimeOffset.UtcNow.ToString("O"));
        int inserted = await insert.ExecuteNonQueryAsync(cancellationToken);

        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = (SqliteTransaction)transaction;
            lookup.CommandText = "SELECT fingerprint, profile, model_id, COALESCE(estimate_id, '') FROM codex_runs WHERE run_id = $run;";
            lookup.Parameters.AddWithValue("$run", run.RunId);
            await using var existing = await lookup.ExecuteReaderAsync(cancellationToken);
            if (!await existing.ReadAsync(cancellationToken) ||
                !string.Equals(existing.GetString(0), run.Fingerprint, StringComparison.Ordinal) ||
                !string.Equals(existing.GetString(1), run.Profile, StringComparison.Ordinal) ||
                !string.Equals(existing.GetString(2), run.ModelId, StringComparison.Ordinal) ||
                !string.Equals(existing.GetString(3), run.EstimateId?.ToString("D") ?? "", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Run ID collision: this identity already refers to a different recording or estimate. Specify a new --run-id.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new IngestResult(inserted == 1, run.RunId,
            run.Complete && run.Usage is not null && run.EstimateId is not null &&
            run.ForecastP50 > 0 && run.ForecastP90 > 0);
    }

    public async Task<HistorySummary> SummarizeAsync(string profile, string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Profile and model ID are required.");
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await InitializeAsync(connection, cancellationToken);
        await using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT complete, input_tokens, output_tokens, forecast_p50, forecast_p90, estimate_id
            FROM codex_runs WHERE profile = $profile AND model_id = $model;
            """;
        query.Parameters.AddWithValue("$profile", profile);
        query.Parameters.AddWithValue("$model", modelId);
        int all = 0;
        int complete = 0;
        var ratios = new List<double>();
        int covered = 0;
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            all++;
            if (reader.GetInt32(0) != 1 || reader.IsDBNull(1) || reader.IsDBNull(2))
            {
                continue;
            }

            complete++;
            if (reader.IsDBNull(3) || reader.IsDBNull(4) || reader.IsDBNull(5))
            {
                continue;
            }

            long p50 = reader.GetInt64(3);
            long p90 = reader.GetInt64(4);
            if (p50 <= 0 || p90 <= 0)
            {
                continue;
            }

            long actual = checked(reader.GetInt64(1) + reader.GetInt64(2));
            ratios.Add((double)actual / p50);
            if (actual <= p90)
            {
                covered++;
            }
        }

        ratios.Sort();
        int matched = ratios.Count;
        double? median = matched > 0 ? (matched % 2 == 1
            ? ratios[matched / 2]
            : (ratios[matched / 2 - 1] + ratios[matched / 2]) / 2) : null;
        return new HistorySummary(profile, modelId, all, complete, matched, median,
            matched > 0 ? (double)covered / matched : null, matched >= MinimumCalibrationSamples,
            matched >= MinimumCalibrationSamples
                ? "Empirical matched-run evidence available; automatic forecast scaling is not enabled."
                : $"Only {matched} matched complete executions. At least {MinimumCalibrationSamples} required before considering calibration.");
    }

    private static async Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS codex_runs (
                run_id TEXT PRIMARY KEY NOT NULL,
                fingerprint TEXT NOT NULL,
                profile TEXT NOT NULL,
                model_id TEXT NOT NULL,
                thread_id TEXT NULL,
                complete INTEGER NOT NULL,
                completed_turns INTEGER NOT NULL,
                failed_turns INTEGER NOT NULL,
                input_tokens INTEGER NULL,
                output_tokens INTEGER NULL,
                cached_input_tokens INTEGER NULL,
                reasoning_output_tokens INTEGER NULL,
                estimate_id TEXT NULL,
                forecast_p50 INTEGER NULL,
                forecast_p90 INTEGER NULL,
                recorded_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_codex_runs_profile ON codex_runs(profile, model_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
