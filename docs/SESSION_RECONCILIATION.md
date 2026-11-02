# Provider session reconciliation — v0.7

AgentForge now has a **local, opt-in, metrics-only** ledger for cumulative provider session usage. It is a separate pipeline from the original Codex JSONL run importer and from ephemeral Copilot per-call usage events.

## Data flow and ownership

1. Forecast a bounded task and record its model/provider, estimate ID and P10/P50/P90 tokens. If a trustworthy matching price is known, also record cost ranges and the exact cost unit.
2. Acquire cumulative usage checkpoints from a provider SDK or an explicitly supplied observation. For Copilot, `CopilotSdkUsageCollector.ReadUsageAsync` reads experimental `session.usage.getMetrics` and returns a cumulative observation without executing a task. The caller owns its authenticated SDK session.
3. Explicitly import checkpoints to `SqliteProviderSessionStore`. The identity key is `(provider, accountId, sessionId)`; prompts, API keys and transcripts are never written.
4. Mark the final snapshot `completed` only when the session is truly finished. Attach a forecast binding only when it refers to the **same execution scope** and only one model was used.
5. Query actual/P50 token ratios and observed P90 coverage for the exact provider/account/model. Cost coverage is calculated only when the evidence is marked verified and uses the compatible provider-specific unit.

The SDK's `assistant.usage` events are **ephemeral per-call counters**, not cumulative checkpoints. The collector marks them `isCumulative: false`, and the ledger rejects them. Never combine event totals with `session.usage.getMetrics`: they can describe overlapping work.

## CLI quick start

From the `TokenBudget` directory, on PowerShell:

```powershell
dotnet run --project .\src\TokenBudget.Cli -- ingest-provider-session `
  .\examples\copilot-session.fixture.json --db .\local-data\provider-sessions.db

dotnet run --project .\src\TokenBudget.Cli -- provider-history `
  --provider copilot --account pjotr-personal --model configured-copilot-model `
  --db .\local-data\provider-sessions.db
```

The checked-in JSON is deliberately **synthetic**: 800 input + 200 output = 1,000 actual tokens, matched to a P50 of 1,000 and P90 of 1,500. It includes eight **provisional** AI credits to exercise accounting, not as a claim about actual Copilot tariffs. Importing once reports `Inserted` with an 800/200/8 delta; repeating the import returns `Duplicate` with a zero delta. Historical token ratio is `1.0`, P90 token coverage is `1.0`, and cost coverage is **null**, because `creditsVerified` is false.

Use a **private, untracked** database path, not a shared repository file. Session IDs and account labels can still reveal work activity. Snapshot JSON input is capped at 1 MiB and the command must be initiated explicitly. Keep the imported forecast binding consistent with the actual run; the ledger cannot infer that an operator linked the correct task.

## Reconciliation rules

| Condition | Behavior |
| --- | --- |
| First cumulative checkpoint | Persist exact account/session key; report total as the initial delta |
| Identical checkpoint replay | `Duplicate`; all usage deltas zero |
| Increasing incomplete-session counters | `Advanced`; report only the increases, with checked monotonicity |
| Completion at unchanged totals | `Finalized`; no new usage charged |
| Changed totals after completion, regressing counters or earlier timestamp with new totals | Reject the import |
| Unknown optional usage subcounter or credit value | Retain prior known value, without claiming newly verified billing |
| Verified credit evidence followed by more tokens without current verification | Preserve historical count; mark its cost verification false |
| Wrong account/provider, changed source stream or lost model identity | Never merge; reject conflicting identity changes |
| Multi-model completed session | Store the observation, but do not attach a single-model forecast binding |
| Missing/experimental credit evidence | Keep reported credits provisional; omit verified cost coverage |

Cached input and reasoning output are **subsets** of total input/output tokens. They are checked for validity and monotonicity when provided but never added to base totals. Monotonic credit values may be updated when a provider's final billing evidence arrives after token completion; this is not a second execution charge.

## Calibration and limitations

- The history summary exposes `medianActualToP50Ratio`, `observedP90TokenCoverage` and, separately, `observedP90CostCoverage` when verified Copilot AI-credit evidence exists. Empty samples return null, **not zero cost or zero coverage**.
- The `sufficientForCalibration` threshold is 20 **completed, explicitly matched, single-model** executions for the same provider/account/model. Reaching the threshold still **does not automatically change P10/P50/P90** or authorize spending.
- Starting in v0.8, use `provider-calibration` for stronger readiness evidence: at least 100 distinct completed matches **and** a conservative 95% Wilson lower bound of at least 90% for observed P90 token coverage. The older 20-run history flag remains descriptive for compatibility and must never be mistaken for verified calibration readiness.
- Copilot SDK `session.usage.getMetrics` supplies model-level token totals. Its nano-AIU representation has an **unverified conversion** here; the collector labels those credits provisional. A user setting `creditsVerified: true` in an import is an explicit evidence assertion, not something this CLI can independently authenticate. Do not do so without a separately verified provider bill or metering source.
- Copilot `account.getQuota` premium interactions are not interchangeable with GitHub AI credits; neither is a ChatGPT subscription limit an OpenAI API bill. Budgets and usage remain provider/account/unit-specific.
- Historical backtesting is observational. It does not prove that P90 predictions are statistically calibrated or that a proposed model has sufficient quality. A live authenticated Copilot account acceptance run and real completed-task history are still needed.

## Developer API

`TokenBudget.Telemetry` defines `SqliteProviderSessionStore`, `ProviderSessionCheckpoint`, `ProviderForecastBinding` and strongly typed reconciliation results. `TokenBudget.Provider.Copilot` defines `CopilotSdkUsageCollector`, accepting a `Func<ProviderAccount, string, CopilotSession?>` to resolve sessions **within the requested account**. Its `ObservePerCall` listener requires the same account/session mapping and never writes to the cumulative ledger.

The new ledger shares no tables with the pre-existing Codex run-id/import-dedup pipeline. There is no background polling, automatic AI execution or plan-content upload.
