# TokenBudget — Technical Architecture & Implementation Handover

**Version:** 1.0  
**Date:** 2026-10-29  
**Target runtime:** .NET 10 / C#  
**Status:** Design approved for implementation planning; not implemented

## 1. Executive decision

Build **TokenBudget** as a provider-neutral, read-only **pre-execution estimator and budget gate**, delivered first as a modular monolith with a .NET library, CLI, minimal HTTP API, and MCP server. Use provider adapters for structured input token counts and execution telemetry, plus an optional LLM-assisted task interpreter. The core engine—not an LLM—calculates budgets and decides pass/warn/fail based on explicit policy.

**Out of scope for v1:** taking control of a ChatGPT Work session; claiming access to undisclosed Work token telemetry; guaranteeing an exact future token total; auto-executing or auto-approving a rejected plan; modifying code as part of estimating it.

## 2. Why a token count alone is insufficient

A one-off token counter measures how big a submitted request is. Agent workflows multiply consumption because each turn can resend instructions, context, tool schemas, file excerpts, and previous messages, and generate output including reasoning tokens. A task with small initial input can have large **cumulative usage** across calls. Keep these four dimensions distinct:

1. **Counted initial input** — known request payload (model and provider specific; exact only to the provider's counting semantics).
2. **Forecast cumulative tokens** — predicted sum of input and output across all model calls, including retries and subagents.
3. **Forecast peak context occupancy** — maximum tokens resident in an individual model request; compaction changes this but does not erase past cumulative usage.
4. **Forecast monetary cost / credit use** — a separate output, with model price schedules, cache categories, hosted-tool cost, and explicit uncertainty. ChatGPT credits must not be converted into raw tokens without validated mapping.

**Accounting invariants:** `TotalTokens = InputTokens + OutputTokens`. Cached input is a subset of input; reasoning output is a subset of output. Never add cached or reasoning tokens a second time. Keep cache reads/writes distinct when providers report them. Unknown or unavailable usage is `null`/unknown, not zero.

## 3. Product workflows

### A. CLI preflight (default)

```powershell
tokenbudget analyze .\docs\handover\arena-plan.md `
  --repo . `
  --provider codex `
  --model <configured-model-id> `
  --max-total-tokens 250000 `
  --max-peak-context-tokens 120000 `
  --format markdown `
  --output .\tokenbudget-report.md
```

CLI writes (a) human-readable report, (b) optional JSON machine report, and (c) nonzero exit code for a policy rejection. It never modifies the repository or runs arbitrary plan instructions.

### B. Work/Codex handover

The user submits a handover document and optionally a repository snapshot or link; TokenBudget returns a structured forecast and optimized, equivalently scoped milestone segmentation. Codex calls it via CLI/MCP before execution. ChatGPT Work invokes it through a supported connected interface **if available** or consumes the report externally; do not assume product-level hooks, internal telemetry access, or automatic enforcement.

### C. API managed agent

The developer's orchestration service calls `POST /v1/estimates`, checks the policy result, requests user approval where appropriate, runs its chosen agent runtime separately, and posts observed usage events to TokenBudget. The estimator remains a separate bounded component.

### D. Historical learning

After a task finishes, ingestion aggregates exact reported usage and classifies completed milestones, file impact, model/provider, tool-call patterns, retries, context compactions, and parallel child agents. A calibrated forecaster compares its prior forecast to the outcome, without treating incomplete runs as normal successful completions.

## 4. Solution structure

```text
src/
  TokenBudget.Domain/              # Immutable domain entities, result semantics, policies
  TokenBudget.Application/         # AnalyzePlan orchestration and use cases
  TokenBudget.Estimation/          # Workload features, simulation, forecast ranges
  TokenBudget.Repository/          # Local Git, read-only repo sampling, Roslyn optional
  TokenBudget.Provider.Abstractions/# Token count, model metadata, telemetry interfaces
  TokenBudget.Provider.OpenAI/     # Responses counting, Responses/Agents usage
  TokenBudget.Provider.Anthropic/  # Claude token count and Messages usage
  TokenBudget.Provider.Codex/      # JSONL CLI, app-server telemetry normalization
  TokenBudget.Provider.Work/       # External handover/report adapter, manual observations
  TokenBudget.Persistence/         # EF Core SQLite initially; DB-portable abstractions
  TokenBudget.Api/                 # ASP.NET Core minimal API + OpenAPI
  TokenBudget.Cli/                 # Command-line front end
  TokenBudget.Mcp/                 # MCP tool façade; no plan execution
  TokenBudget.Observability/       # Metrics, traces, structured logging

tests/
  TokenBudget.Domain.Tests/
  TokenBudget.Estimation.Tests/
  TokenBudget.Application.Tests/
  TokenBudget.Provider.Tests/
  TokenBudget.Contract.Tests/
  TokenBudget.Integration.Tests/
  TokenBudget.Backtesting.Tests/
```

No microservices, Dapr, Redis, Service Bus, or Kubernetes are required for v1. Optional future workers should be added only when ingestion throughput or organizational boundaries justify them.

## 5. Core ports and canonical C# contracts

The following contracts establish the design, not a claim of compiled implementation. Type and naming details are to be finalized with tests during M1.

```csharp
namespace TokenBudget.Domain;

public enum ExecutionTarget
{
    OpenAiResponses,
    OpenAiAgents,
    CodexCli,
    CodexAppServer,
    ChatGptWork,
    AnthropicMessages
}

public enum ForecastConfidence { Low, Medium, High }
public enum BudgetDecision { Pass, Warning, Reject, InsufficientEvidence }
public enum MeasurementSource { ProviderCount, LocalTokenizer, Heuristic, ReportedUsage, Manual }

public sealed record BudgetConstraints(
    long? MaxCumulativeTokens,
    long? MaxPeakContextTokens,
    decimal? MaxApiCostUsd,
    double RiskTolerance = 0.10);

public sealed record AnalyzePlanRequest(
    string Plan,
    string ModelId,
    ExecutionTarget Target,
    BudgetConstraints Budget,
    string? RepositoryPath = null,
    string? RepositoryRevision = null,
    string? HistoricalProfileId = null,
    bool AllowProviderUpload = false);

public sealed record TokenRange(long P10, long P50, long P90);

public sealed record UsageBreakdown(
    long InputTokens,
    long OutputTokens,
    long? CachedInputTokens,
    long? CacheWriteInputTokens,
    long? ReasoningOutputTokens);

public sealed record MilestoneForecast(
    string Id,
    string Description,
    TokenRange CumulativeTokens,
    TokenRange PeakContextTokens,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Assumptions);

public sealed record EstimateReport(
    Guid EstimateId,
    DateTimeOffset GeneratedAt,
    ExecutionTarget Target,
    string ModelId,
    long? CountedInitialInputTokens,
    MeasurementSource InitialInputCountSource,
    TokenRange CumulativeTokens,
    TokenRange PeakContextTokens,
    ForecastConfidence Confidence,
    BudgetDecision Decision,
    IReadOnlyList<MilestoneForecast> Milestones,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Recommendations,
    string EstimatorVersion);
```

Application and infrastructure ports:

```csharp
public interface IPlanAnalyzer
{
    Task<PlanGraph> AnalyzeAsync(AnalyzePlanRequest request, CancellationToken cancellationToken);
}

public interface IRepositoryImpactAnalyzer
{
    Task<RepositoryImpact> InspectAsync(string repositoryPath, PlanGraph graph, CancellationToken cancellationToken);
}

public interface IInputTokenCounter
{
    bool Supports(ExecutionTarget target, string modelId);
    Task<TokenCountResult> CountAsync(TokenCountRequest request, CancellationToken cancellationToken);
}

public interface ITokenForecastEngine
{
    Task<EstimateReport> ForecastAsync(ForecastInputs inputs, CancellationToken cancellationToken);
}

public interface IBudgetPolicy
{
    BudgetDecision Evaluate(EstimateReport report, BudgetConstraints constraints);
}

public interface IUsageIngestor
{
    Task IngestAsync(UsageObservation observation, CancellationToken cancellationToken);
}

public interface ICalibrationRepository
{
    Task<CalibrationProfile?> GetAsync(CalibrationKey key, CancellationToken cancellationToken);
}
```

`PlanGraph`, `RepositoryImpact`, `TokenCountRequest/Result`, `ForecastInputs`, `UsageObservation`, `CalibrationKey/Profile` are immutable domain records defined within the relevant module. All external request/response DTOs are separate from domain types.

## 6. How plan decomposition works

**Pass 1 — deterministic parser:** parse Markdown headings, tables, checkbox lists, explicit milestone IDs, references, acceptance gates, file mentions, test instructions, dependencies, target branch, stop conditions. Produce graph nodes and edge provenance (which exact source span yielded each node).

**Pass 2 — optional structured LLM parser:** enabled only when deterministic parsing cannot reliably resolve implicit tasks. Return strict JSON validated against a schema. Its output is **untrusted proposed structure**: validate constraints, reference spans, duplicates, dependency cycles, and consistency. Limit inputs and count this agent's analysis cost separately. No code execution and no shell commands in this parser.

**Pass 3 — preserve invariants:** no invented acceptance criteria, no dropping milestones, no silently weakening visual/approval gates. Split tasks only at dependency-safe boundaries. A rewrite must come with a coverage map from original requirements to retained requirements and an explicit list of semantic differences (ideally empty).

Task feature extraction includes: requirement count, affected subsystems, estimated changed/read files, changed lines, unfamiliar dependencies, external services, required tests, visual proof, build/test loops, expected debugging cycles, and number of likely delegated agents.

## 7. Repository impact and cost control

Inspect Git metadata and repository files in **read-only mode**. Prefer user-provided checkout/snapshot over cloning. Skip ignored directories, build output, binaries, packages, credentials, and oversized files. Apply deterministic path limits and size budgets. For C# repositories, a Roslyn-based graph can optionally identify project references, symbols, and transitive impacts; it is not required for first release.

Impact analysis should return `min/likely/max` file reads and writes with evidence, not pretend it knows future edits exactly. Record unknown scope and boost upper forecast tail. Without repository access, show a lower confidence and use explicit user-specified impact assumptions.

Security constraints: no `dotnet restore`, test execution, shell scripts, build hooks, or repository write during **analysis**. A local Git CLI inspection can be restricted to safe read-only subcommands, or replaced with direct file parsing. Never upload repository source to a third-party counter without explicit opt-in.

## 8. Forecast model

### 8.1 Stage-level workload units

A plan is a DAG of milestone stages, each decomposed into operations:

- Read/inspect code or documents
- Reason and plan
- Implement or refactor
- Build/test and inspect output
- Debug/retry
- Verify acceptance and report
- Delegate/synchronize subagents

For each operation, estimate distributions for model-call count, retained input context, context compaction, tool result size, generated output, cache ratio, retry count and subagent work. Add conditional branches for failures. Use conservative defaults and **explicit priors** before sufficient observed history is available.

### 8.2 Equations

For model call `i`:

```text
T_i             = I_i + O_i
I_i             = Instructions_i + TaskContext_i + RepositoryContext_i
                  + ToolSchemas_i + History_i + ToolResults_i + OtherInputs_i
O_i             = VisibleOutput_i + ReasoningAndProtocolOverhead_i
TotalRunTokens  = SUM(T_i) over root and all subagent model calls
PeakContext     = MAX(I_i + reservedOutputAllowance_i) over all model calls
```

Prevent duplicate summation when provider usage is a cumulative snapshot. Treat extra model overhead that is not reported in separate fields as part of the authoritative provider totals. Do not subtract cached tokens from `TotalRunTokens`; caching affects **price**, not token count. Count supplementary hosted tool/sandbox fees separately from model token charges.

### 8.3 Forecast quantiles

Generate at least 10,000 **seeded Monte Carlo samples** of task-stage distributions, with correlated sampling for uncertainty drivers (large codebase, poor tests, unfamiliar subsystem). Sum across the dependency graph, calculate P10/P50/P90 cumulative tokens and P90 peak context, and provide milestone-level attribution. Parallel work lowers elapsed time but not necessarily cumulative tokens.

A deterministic fallback can use analytically calculated ranges when calibration data is absent. Initially output LOW confidence with wide bounds; do not invent precise-looking point estimates. Quantiles are conditional estimates, not guarantees.

### 8.4 Confidence and calibration

Partition observations by `(provider, model, execution target, workload type, repository scale, reasoning mode)` when sample size permits; use hierarchical fallback across sparse segments. Record `estimator_version` and immutable snapshot of calibration parameters. Improve model using observed calls, retries, tool-output size, and status. Track P90 coverage (target >= 90% on valid held-out runs), P50 median error, context overrun rate, underforecast severity, and sample size. Until calibrated, show `confidence: Low` and list priors used.

Use distinct censored-run handling for cancellations, rate limits, forced stops, missing subagent usage, partial tasks, and user approvals. Never count truncated execution as a normally completed run.

## 9. Decision and budget policies

**Inputs:** cumulative-token budget, peak-context-token budget, optional USD API-cost budget, known provider limits, uncertainty tolerance, user-specified hard gates.

Default risk policy (configurable):

- **Pass:** enough evidence and forecast P90 <= 85% of each applicable budget, with no known peak context violation.
- **Warning:** enough evidence and P90 between 85% and 100%, or meaningful dependencies/approval gates likely to interrupt.
- **Reject:** P90 exceeds a hard budget or a known model context limit; recommend a smaller DAG-preserving execution slice.
- **InsufficientEvidence:** required budget/model data or repo access is missing, or output telemetry coverage is too weak to justify an authoritative gate; return assumptions rather than a misleading green result.

For context-window decisions, include reserved output and system/tool overhead; validate actual limits against provider metadata/config, never hardcode one generic number. `RiskTolerance` controls the percentile selected for approval; by default, 10% overrun risk implies using P90.

**Approval semantics:** pass is a forecast, not permission to execute. Budget validation and human acceptance gates are orthogonal.

## 10. Provider integration design

### OpenAI Responses

Use `POST /v1/responses/input_tokens` with the same model, tools, messages, media, and instructions shape where supported; collect `input_tokens`. Ingestion parses `usage.input_tokens`, `usage.output_tokens`, cached and reasoning subsets when present. Count all root and child calls in systems under our control.

### OpenAI Agents API

Use session/turn resources and public usage/events or trace exports. Collect root and subagent turns. Usage may be unknown or later revised; store as upsertable observations. Session aggregates must **not** be added to their constituent turns. Agents API is a separate managed-runtime option, not an undocumented Work connector.

### Codex CLI

For initiated or externally recorded runs, parse `codex exec --json` JSONL. Accept `turn.completed.usage` as run/turn usage, `thread.started` as correlation, and `turn.failed` as termination. Normalize event schema versions and tolerate missing optional fields. Do not rely on undocumented rollout-log parsing.

### Codex app-server

Use the documented client protocol and `thread/tokenUsage/updated` notifications where available. These may be cumulative snapshots: compute deltas against the last authoritative snapshot for the same thread, not naive additive aggregation. Reconcile completed-turn totals when present and preserve raw source provenance.

### Anthropic Messages

Use `POST /v1/messages/count_tokens` for supported request shapes; don't assume support for every server-side tool/MCP/media encoding. Ingest actual message usage, cache-creation/cache-read details when reported, and provider-specific reasoning semantics. Exact counting is conditional on request shape acceptance.

### ChatGPT Work

Support **preflight-only** through report, API, or MCP invocation when a supported interface is available. The internal execution engine, token usage, and enforcement hooks are not public integration assumptions. Optionally accept user-exported/report-provided credit and estimated-cost observations, store them in separate units and mark the source. Never equate credits with API-token totals.

### Future providers

Add adapters to existing interfaces without changing forecast/policy code. Maintain a provider capability registry: `CanCountExactInput`, `CanIngestUsage`, `SupportsMedia`, `SupportsReasoningBreakdown`, `SupportsCacheBreakdown`, `CanEnforcePreflight`, and `SupportsAutomaticTelemetry`.

## 11. External interfaces

### HTTP

```text
POST /v1/estimates                -> 202/200 estimate/report with data provenance
GET  /v1/estimates/{id}           -> saved result
POST /v1/estimates/{id}/optimize  -> optional coverage-preserving plan segmentation
POST /v1/executions/usage         -> idempotent usage observation ingestion
GET  /v1/calibration/summary      -> aggregated performance and coverage
GET  /health                     -> liveness
```

A synchronous `POST /v1/estimates` is adequate for v1 bounded input; return 202 and operation ID only for long-running repository analysis. Limit payload sizes and require scoped auth outside local-only deployments.

### MCP (read-only initially)

```text
tokenbudget_analyze_plan
  input: plan, model, target, budgets, optional repo path/reference
  output: typed estimate JSON + narrative summary

tokenbudget_optimize_plan
  input: estimate_id, preservation_constraints
  output: revised milestone graph + requirement coverage + risk comparison

tokenbudget_get_estimate
  input: estimate_id
  output: immutable estimate report
```

Do not expose `run_plan`, `git_push`, or code-modifying operations from the MCP module. An optional separate authenticated `tokenbudget_record_usage` tool can be added for trusted orchestrators, not untrusted model instructions.

### CLI

```text
tokenbudget analyze <plan.md> --repo <path> --provider <target> --model <id> --max-total-tokens <n>
tokenbudget optimize <plan.md> --estimate <id> --output <optimized.md>
tokenbudget ingest codex-jsonl <run.jsonl>
tokenbudget backtest --profile <id> --format markdown
```

Exit codes: `0=pass`, `2=warning`, `3=reject`, `4=insufficient evidence`, `1=operational error`. Consider a `--warnings-as-errors` switch for CI. Do not silently turn missing provider access into a passing estimate.

## 12. Persistence and observability

Minimum entities:

- `Estimate`: plan checksum, repo revision, model, target, quantiles, decision, versions, timestamps
- `StageEstimate`: stage id, dependencies, assumptions, read/write impact, token ranges
- `Execution`: run id, target, model, completion status, estimated/observed links
- `UsageObservation`: provider event id, run/turn/agent id, counters, source, revision/time, raw/snapshot semantics
- `CalibrationProfile`: segment key, sample size, distribution parameters, validity/effective date
- `RequirementMapping`: original source spans to optimized milestones

Use EF Core + SQLite initially. Apply unique constraints to `(source, provider_run_id, provider_turn_id, observation_revision_or_event_id)` and transactional upserts. Distinguish **snapshot** from **delta** semantics in the normalized event type. Keep generated forecasts immutable; new calibration produces a new estimate version.

OpenTelemetry traces: analyze plan, repository inspection, token count, simulation, budget evaluation, usage ingestion. Metrics: count latency, forecast/actual ratio, P90 coverage, unknown usage percentage, cache hit ratio, duplicate event rejection, stage split suggestions. No source code, token keys, access tokens, or prompt payloads in ordinary log fields.

## 13. Optimization algorithm

Recommend changes only if they preserve task semantics:

1. Reuse explicit milestone boundaries and dependencies.
2. Group work by relevant repository subtree and test area to reduce repeated discovery.
3. Replace repeated full-file reads with scoped retrieval, but do not truncate necessary acceptance evidence.
4. Set per-stage context budgets and compaction/continuation checkpoints.
5. Do not replace mandatory tests, visual review, or real-world validation with a cheaper assertion.
6. Optimize in terms of **completion quality per token**, not just minimum token count.

For each proposed split, report: projected P50/P90 savings (if supported by data), additional handoff overhead, requirement coverage %, critical dependencies, human approval gates, and assumptions. Re-running common context per stage can increase tokens: show that tradeoff rather than always suggesting smaller prompts.

## 14. Security, privacy, and governance

- Read-only repo inspection; path-root isolation; no arbitrary command execution.
- Treat submitted plans, file contents, tool results, and git data as untrusted instructions.
- Opt-in transmission of source, prompts, attachments, or plan text to external token-counting APIs.
- Never send secrets or private files to provider counters by default; support local tokenizer fallback with reduced accuracy.
- Provider credentials in secure configuration/env or a secret store, not persisted into reports or DB.
- Limit request sizes, recursion, file scanning, JSONL event size, and MCP rate.
- Usage ingestion restricted to trusted clients; deduplicate replayed events and prevent cross-tenant mixing.
- Default persistence stores hashes/metadata/counts; store raw prompts/paths only if explicit retention policy allows.
- Separate budget gate from authorization: even a PASS never authorizes actions that have a human approval gate.

## 15. Delivery milestones and hard gates

**M0 — Baseline & provider contracts**
- Document provider payload shape, availability, capability registry, and failure semantics.
- Fixture coverage for OpenAI, Anthropic, Codex CLI, and Codex app-server.
- **Gate:** no unsupported claim of universal exact future usage or internal Work access.

**M1 — Domain and deterministic estimation**
- .NET 10 solution, CPM (`Directory.Packages.props`), domain models, Markdown parser, heuristic defaults, basic Monte Carlo and policy engine.
- **Gate:** deterministic seeded forecasts; 100% accounting invariants; all tests green.

**M2 — Repository impact analyzer**
- Read-only Git indexing, file and project references, bounded context size estimator; C# Roslyn enhancement if justified.
- **Gate:** handles large/malformed repos within resource limits; never writes or executes repo content.

**M3 — Provider counting and instrumentation**
- OpenAI/Anthropic input count, Responses usage, Codex JSONL, app-server snapshots and Agents API turn data.
- **Gate:** no duplicate counts, no null-as-zero, cached/reasoning tokens not double counted.

**M4 — CLI, REST API and MCP**
- `analyze`, `optimize`, `ingest`, `backtest`; Minimal API endpoints; read-only MCP tools; local docs.
- **Gate:** a coding agent can request a preflight result without executing the submitted plan.

**M5 — Optimization, history and calibration**
- Stage-preserving split proposals, requirement mapping, SQLite, data ingestion and stratified backtests.
- **Gate:** regression suite protects requirements and calibration improves over static priors on held-out examples.

**M6 — Integration acceptance and operational hardening**
- Real Codex execution telemetry capture, OpenAI API run, Anthropic counting, ChatGPT Work preflight report demonstration; CI policy.
- **Gate:** end-to-end demos and documented unsupported Work capabilities; alerting/metrics; privacy review.

Avoid automatic progression past an explicit user sign-off or destructive integration gate. Implementation can proceed through the unblocked milestones in a single development run.

## 16. Acceptance test matrix

| Scenario | Expected behavior |
|---|---|
| 2,000-token known input | Correct provider count (or labeled local estimate) |
| 20 small tasks vs 1 large migration | Different workload forecast with evidence |
| Multiple model calls with repeated context | Cumulative usage counted across all calls |
| Cached input and reasoning output | Count subsets only once; cache priced separately |
| Parallel subagent turns | Sum all known agent usage; do not mistake speedup for token reduction |
| Codex JSONL replayed twice | Idempotent ingestion; unchanged totals |
| App-server cumulative usage updates | Delta/snapshot reconciliation; no overcount |
| Agents API usage delayed/null | Incomplete observation clearly labeled, never zero |
| Missing repo | Low confidence / explicit evidence gap |
| User-provided Markdown prompt injection | Treated as task data, never executed |
| Hard token budget below P90 | Warn/reject by policy; proposed safe split |
| Peak model context too large | Reject even if total budget large |
| Optimized plan drops a requirement | Optimization rejected by coverage validator |
| User approval gate in original plan | Preserved in all rewrites |
| Canceled/partial run | Excluded from naive success calibration |
| Work credits uploaded | Separate credit metadata; no fabricated token conversion |

## 17. Illustrative report (not a real estimate)

```json
{
  "target": "CodexCli",
  "modelId": "<configured-model-id>",
  "countedInitialInputTokens": 4200,
  "initialInputCountSource": "ProviderCount",
  "cumulativeTokens": { "p10": 45000, "p50": 85000, "p90": 160000 },
  "peakContextTokens": { "p10": 22000, "p50": 48000, "p90": 95000 },
  "confidence": "Low",
  "decision": "Warning",
  "risks": ["Unknown repository scope", "Uncalibrated debugging retries"],
  "recommendations": ["Separate rendering verification from refactor", "Set a per-stage read budget"],
  "estimatorVersion": "1.0.0"
}
```

This is purely illustrative. Production reports must show the configured budget and provenance, and must never identify invented numbers as measured statistics.

## 18. Documentation consulted (2026-10-29)

- OpenAI, Counting tokens: https://developers.openai.com/api/docs/guides/token-counting
- OpenAI, Agents API observability and usage: https://developers.openai.com/api/docs/guides/agents-api/observability
- OpenAI, Codex `exec --json` and token usage event example: https://learn.chatgpt.com/docs/non-interactive-mode
- OpenAI, Codex app-server and `thread/tokenUsage/updated`: https://learn.chatgpt.com/docs/app-server
- Anthropic, Token counting: https://platform.claude.com/docs/en/build-with-claude/token-counting
- OpenAI, Work and Codex usage display: https://help.openai.com/en/articles/20001478

## 19. Recommended first deliverable

Implement **M0–M1** as the first vertical slice. It must accept a Markdown handover, produce a structured `EstimateReport` with P10/P50/P90, distinguish cumulative usage from peak context, apply a budget gate, and return deterministic seeded results with clearly labeled uncertainty. Then proceed to M2–M6, grounding forecast quality in real execution measurements rather than invented precision.
