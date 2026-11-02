# Provider-aware budgeting (v0.6 foundation through v0.9 live diagnostics)

TokenBudget/AgentForge now separates provider-neutral `EstimateReport` token scenarios from billing policy and provider-specific ledgers. It estimates token categories independently, then prices those scenarios in the selected provider's unit. Quantiles for input and output are computed independently; their sum is conservative but is not the P90 of a directly sampled joint cost distribution. Historical outcomes are still needed to calibrate confidence.

## Project ownership

| Component | Responsibility |
| --- | --- |
| `TokenBudget.Domain` | Original plans, dependencies, approvals, P10/P50/P90 token ranges and newly reported input/output token categories |
| `TokenBudget.Application` | Seeded, offline token simulation and read-only repository metadata analysis; contains **no** model prices |
| `TokenBudget.Billing` | Account-scoped quota contracts, decimal cost accounting, P90 policy, model-price comparison and safe prompt-scope suggestions |
| `TokenBudget.Provider.Copilot` | Official `GitHub.Copilot.SDK` 1.0.27: `models.list`, `account.getQuota`, `session.usage.getMetrics`, `assistant.usage`, and experimental session quota observations |
| `TokenBudget.Cli` | `budget` command with current Copilot SDK catalog opt-in and explicitly supplied offline snapshots |
| `TokenBudget.Api` | Authenticated `POST /v1/provider-budgets` accepting strongly typed rate/quota snapshots |
| `TokenBudget.Telemetry` | v0.7 opt-in account/session-scoped cumulative ledger and actual-versus-forecast backtesting; Codex run ledger remains independent |

`IProviderPricingProvider`, `IProviderPricingCatalog`, `IProviderBudgetAdapter`, and `IProviderUsageCollector` are all injected. Offline adapters are scoped by provider, account ID and exact unit. The SDK client is provided by its owner; nothing in the engine starts an AI execution or sends the plan to Copilot.

## Billing units and data provenance

- **Token usage**: provider-neutral workload projection; input, output, and optionally cached input are distinguished. Caching share is zero in the conservative default, and a nonzero share must be explicitly configured. Cached tokens are a subset of input, not an additional bucket.
- **GitHub AI credits**: Copilot metered billing. The official account value convention is **1 AI credit = $0.01 in metered usage value**, *not* 1 token and *not necessarily an additional invoice charge*. Per-model input/output/cache price is loaded dynamically from SDK `models.list` or explicitly supplied. Never ship a fixed tariff table.
- **Premium interactions**: `account.getQuota` commonly reports `premium_interactions` counts and reset metadata. Those are not silently converted into AI credits. Legacy request-based billing therefore remains **cost unknown** until a compatible request-count forecaster and charging rules exist.
- **USD**: separately configured OpenAI/Anthropic API prices by input/output/cache category, plus optionally configured non-token charges. Price lists are not treated as static universal defaults. This is metered API usage, not subscription charges.
- **ChatGPT/Codex subscriptions**: advisory quota units only. Without an authoritative live metering API, no exact credit conversion is claimed. An unknown allowance is never reported as zero.
- **AgentForge's optional reviewer**: an external OpenAI API call uses a *separate* OpenAI account/budget. The `budget` command does not invoke it, so its additional future cost is **unmeasured**, not added to GitHub credits. Reviewers cannot alter deterministic decisions.

`BudgetSnapshot` records allowance, used, explicitly reported remaining, reset date, source and timestamp. A negative balance is rejected, a missing balance remains unknown, an expired reset period is stale, and the `-1` unlimited sentinel is represented as `Unlimited`, not a negative allowance.

Session quota observations are exposed without promoting advisory or admission-only states to a monetary balance; `CopilotSdkSessionQuotaReader` has explicit `refresh` control. Experimental SDK members use targeted `GHCP001` suppressions. The package version is pinned because session quota and metrics shapes may change.

**v0.7 actual usage:** `session.usage.getMetrics` produces a cumulative session observation, while `assistant.usage` reports individual events. The new ledger permits only cumulative snapshots to prevent double counting. Its Copilot nano-AIU conversion is provisional and does **not** qualify as verified AI-credit cost evidence. Verified cost backtesting, when a trustworthy source is supplied, remains separate from token comparisons; null means unavailable, not zero. See [session reconciliation](SESSION_RECONCILIATION.md).

**v0.9 live diagnostics:** `copilot-probe` performs SDK billing lookups only with explicit caller consent. It reports model pricing, Copilot AI credits and premium-interaction quotas independently, and preserves partial/unknown states. The `--account` label is **not** independently verified to match the SDK-authenticated identity. No account lookup was performed during the local test run; see [v0.9 release](RELEASE_v0.9.md).

## Safe policy outcomes

| Result | Deterministic interpretation |
| --- | --- |
| `Execute` | P90 cost fits the remaining allowance and task reserve; no model-quality guarantee or execution permission |
| `ReserveBudget` | Meaningful share of allowance at risk; plan testing/retry reserve |
| `NarrowScope` | P90 risk exceeds a configured share or the original token policy warns |
| `Defer` | P90 exceeds the account, reserve, or task cap, or the existing token policy rejects |
| `InsufficientEvidence` | Missing/stale prices or balance, or original estimate cannot support a budget conclusion |

The policy always preserves milestones requiring human approval. The prompt advisor can select one dependency-free milestone and append safer execution boundaries, while retaining the complete original plan **verbatim**. Candidate cheaper models are price comparisons only; verify feature/tool support, context size and quality before switching.

## Offline CLI — illustrative user-configured rates

The values for `--price-input`, `--price-output` and `--price-batch` **below are synthetic for exercising the workflow, not actual Copilot model prices**. Replace them with a current `models.list` quote or use `--live-copilot true` after authenticating your own SDK client.

```powershell
dotnet run --project .\src\TokenBudget.Cli -- budget .\examples\wyrmforge-g6-budget-plan.md `
  --provider copilot --account pjotr-personal --model your-copilot-model-id `
  --allowance 30000 --used 9000 `
  --price-input 0.75 --price-output 4.00 --price-batch 1000 `
  --max-total-tokens 250000 --reserve-units 1000 --risk-limit 0.50 `
  --improve-prompt true --format markdown
```

This records the **account-reported** balance of 21,000 credits; it never assumes all Copilot users have 30,000 credits. `--reset-utc 2026-12-01T00:00:00Z` is optional *only when known from your account*, otherwise the report says `unknown`. For live model pricing add `--live-copilot true` and omit the three manual price arguments. The SDK may still lack a metered AI-credit quota; provide `--allowance`/`--used` in that case. The actual price and output will vary with the model and prompt.

The command exits `0` execute, `2` caution/narrow, `3` defer, `4` insufficient evidence, or `1` invalid input. It never executes the plan. For JSON, use `--format json`. To avoid uploading any prompt, leave `--allow-provider-upload` unset (default false). Copilot billing lookup only reads the model/account catalog.

## HTTP API

`POST /v1/provider-budgets` reuses the existing loopback/API-key security, request size limit and repository allowlist. Example request body (synthetic rate, safely configured account):

```json
{
  "plan": { "plan": "## M1 - Validation\nBuild and test.", "modelId": "your-copilot-model-id", "target": "GitHubCopilot", "budget": { "riskTolerance": 0.1 } },
  "account": { "provider": "GitHubCopilot", "accountId": "pjotr-personal" },
  "policy": { "maxTaskUnits": 5000, "reservedUnits": 1000, "maxAtRiskFraction": 0.5 },
  "assumptions": { "assumedCachedInputFraction": 0 },
  "pricing": { "provider": "GitHubCopilot", "modelId": "your-copilot-model-id", "mode": "MeteredAiCredits", "unit": "GitHubAiCredits", "inputPerBatch": 0.75, "outputPerBatch": 4.0, "batchTokens": 1000, "observedAt": "2026-11-02T08:00:00Z", "source": "illustrative-configured-rate" },
  "quota": { "account": { "provider": "GitHubCopilot", "accountId": "pjotr-personal" }, "unit": "GitHubAiCredits", "allowance": 30000, "consumed": 9000, "observedAt": "2026-11-02T08:00:00Z", "source": "explicitly-account-reported" },
  "improvePrompt": true
}
```

The API intentionally accepts snapshots instead of starting SDK account sessions for arbitrary HTTP callers. Deploy only behind authenticated TLS if remote access is required; the current dev server is local-first.

## Remaining gaps

1. Verify SDK end to end with a consenting authenticated Copilot account, including runtime/CLI version, prices, `session.usage.getMetrics` and quota observations; no credentials are included in this source archive.
2. Live OpenAI/Anthropic pricing catalogs, per-provider usage collectors and account balance acquisition still require a verified supported source; manually supplied current prices work now.
3. Legacy Copilot premium request charging and subscription reset/limit semantics are **not** inferred from token totals.
4. Empirically calibrate joint token/cost quantiles and model suitability from real, deduplicated historical runs. The separate v0.7 provider session ledger supports preliminary backtesting, but no real matched dataset or automated tuning exists. Existing SQLite Codex telemetry and idempotency behavior are left unchanged.
