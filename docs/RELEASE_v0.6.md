# TokenBudget v0.6 — provider-aware budgeting foundation

Date: 2026-11-02. Release candidate: code compiled and tested locally; no live billing account acceptance yet.

## Delivered

- `TokenBudget.Billing` project with provider/account/unit-scoped contracts, account-reported and unavailable adapters, model catalogs and decimal P10/P50/P90 cost conversion.
- Token forecaster now provides separately modeled input/output category quantiles without changing existing aggregate token estimates or policies. Cached input defaults to an explicitly stated zero-cache assumption.
- Deterministic provider decision layer: `Execute`, `NarrowScope`, `ReserveBudget`, `Defer`, `InsufficientEvidence`, with task cap, P90 allowance check, reserve, max at-risk fraction, quota and pricing freshness, reset validity, and approval gate preservation.
- GitHub Copilot adapter compiled against official `GitHub.Copilot.SDK` 1.0.27 for dynamic `models.list` tariffs, account `premium_interactions` quota, `session.usage.getMetrics`, per-call `assistant.usage`, and experimental `session.quota` get/refresh observations. No model rates are embedded in code.
- New `tokenbudget budget` CLI with optional SDK lookups, explicitly reported per-account balances, synthetic/user-supplied pricing fallback, Markdown/JSON, improved prompt and price-only model alternatives.
- New authenticated `POST /v1/provider-budgets` API endpoint. No remote prompt or content upload by default and no task execution.
- Read-only scope advisor keeps all original requirements and approval gates verbatim, and selects one dependency-free milestone when the scope is broad.
- New MSTest coverage for prices, caches, legacy mode, null/stale quotas, rounding, reset, independent account balances, budget policy, prompt retention and model candidates.
- Original telemetry deduplication and optional AI reviewer behavior remain unchanged.

## Quality gate

Verified 2026-11-02: **17/17 projects** compile in .NET 10 Release with **zero warnings and zero errors**, **100/100 MSTest** cases pass. New billing test project has 31 cases; 69 existing tests still pass. Real CLI example produces 98.3705 / 166.0115 / 259.8890 AI-credit P10/P50/P90 using explicitly synthetic prices and a configured 21,000 credit remaining account balance; these are not official current model rates. The loopback `POST /v1/provider-budgets` returned HTTP 200 to a synthetic test payload. Re-run:

```powershell
dotnet restore .\TokenBudget.slnx
dotnet build .\TokenBudget.slnx -c Release --no-restore -m:1
dotnet test .\TokenBudget.slnx -c Release --no-build -m:1
```

## Explicit limitations

- Account live Copilot quota/usage and model pricing could not be acceptance-tested against user credentials; initial verification is compile-time and SDK contract compatibility only.
- SDK `account.getQuota` often exposes premium interactions, not verified metered AI-credit balance; missing credit balance is unknown, never zero. Legacy request tariffs cannot be inferred from model token rates.
- Input/output token marginals are combined conservatively; P90 has not been empirically calibrated as a precise 90% cost coverage probability.
- OpenAI and Anthropic APIs accept dynamic/configured external rate snapshots, but live price retrieval/account billing feeds are not yet implemented. Codex/ChatGPT subscription charging remains advisory.
- The optional OpenAI AI reviewer has a separate account/cost and is never silently financed from the Copilot allowance.
- Shipping the SDK may download its bundled Copilot CLI runtime for the selected platform on build; ensure runtime/SDK versions are pinned and available in CI.
