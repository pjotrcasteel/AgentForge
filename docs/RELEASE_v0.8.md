# AgentForge / TokenBudget v0.8 — Evidence-based calibration diagnostics

**Date:** 2026-11-02  
**Scope:** Descriptive, provider/account/model-specific statistical assessment of completed executions. No AI execution, upload, automatic price update or forecast scaling.

## New functionality

- `ProviderCalibrationAnalyzer` is a pure .NET 10 evaluator, separated from SQLite, provider billing adapters and the existing deterministic estimation engine.
- For each completed, uniquely identified, single-model execution matched to an estimate, it computes observed **P10, P50 and P90 token coverage**, median actual/P50 usage and a **95% Wilson lower bound** for P90 coverage.
- Historical evidence remains strictly isolated by provider, account ID and model ID. Replaying a session cannot inflate the statistical sample.
- A low observed P90 hit rate after at least 20 comparable runs flags `InvestigateUnderforecast`. A calibration `ReviewCandidate` requires at least **100 distinct comparable runs** **and** a Wilson lower bound of **at least 90%**. This is an advisory review gate, not a guarantee or automatic adjustment.
- Independently evaluates **verified Copilot AI-credit P90 coverage**. Provisional nano-AIU values, incompatible units and other providers' usage do not qualify as verified credits. Missing coverage is `null`, not zero.
- `SqliteProviderSessionStore.AnalyzeCalibrationAsync` reads the v0.7 metrics-only ledger without changing any records. `provider-calibration` exposes the report as JSON in the CLI.
- The existing `provider-history` contract and historical 20-run descriptive threshold are preserved. They do not imply that automatic calibration is safe.

## CLI example

```powershell
dotnet run --project .\src\TokenBudget.Cli -- ingest-provider-session `
  .\examples\copilot-session.fixture.json --db .\local-data\provider-sessions.db

dotnet run --project .\src\TokenBudget.Cli -- provider-calibration `
  --provider copilot --account pjotr-personal --model configured-copilot-model `
  --db .\local-data\provider-sessions.db
```

The included fixture is **synthetic**, with one matched session and eight **unverified** Copilot AI credits. Expect `CollectMoreEvidence`, one completed match, 100% observed P90 token coverage from this **one sample**, a substantially lower statistical confidence bound and **null** verified-credit coverage. Repeated imports remain idempotent.

## Verification

- **17/17 .NET 10 projects compiled in Release with zero warnings or errors; 147/147 MSTest cases pass** (21 added since v0.7).
- New tests cover small-sample caution, poor coverage, 99-versus-100-run evidence gates, duplicate run IDs, account/model boundaries, quantile arithmetic, provisional versus verified credits, unit separation and CLI diagnostics. No live provider calls are made by the tests.
- A real local CLI invocation against the synthetic fixture returned one matched run, P90 token coverage `1.0`, conservative 95% lower bound `0.20654931437723809`, `CollectMoreEvidence` and null verified-credit coverage. The read-only calibration command refuses to create a missing database.

## Outstanding work

1. Verify live Copilot metering and billing provenance with explicit account consent; no credentials are bundled or read by these commands.
2. Collect real matched execution history across task types. Statistical evidence from synthetic fixtures **does not validate** empirical forecast accuracy.
3. Consider bounded, human-reviewed model-specific calibration only after independent checks of sample quality, drift, selection bias, task similarity and actual future coverage. Never allow an AI reviewer to override deterministic budget rules.
4. Expand independent usage collectors and current-price adapters for API providers. ChatGPT/Codex subscription limits remain advisory where metering is unavailable.
