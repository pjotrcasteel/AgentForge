# AgentForge / TokenBudget v0.9 — Copilot account probe foundation

**Date:** 2026-11-02  
**Scope:** Consent-gated, read-only GitHub Copilot SDK billing diagnostics with no prompt execution or credential persistence.

## Implemented

- New `copilot-probe` CLI command queries the authenticated SDK's model pricing and account quota only after `--allow-live-account-lookup true` is supplied. It never creates an AI session or sends a development prompt.
- `CopilotBillingProbeService` uses strongly typed, injected pricing and quota ports. Each lookup fails independently, so missing model prices do not erase available quota evidence and vice versa.
- `CopilotBillingProbeReport` keeps three signals separately classified: model-price availability, **GitHub AI-credit** balance, and **premium-interaction** quota. The two latter units are never interchangeable.
- The account ID in the report is an operator-provided label. The SDK APIs used here do **not** verify that the label matches the authenticated account; the result explicitly reports `authenticatedAccountIdentityVerified: false`.
- Missing quotas stay **unavailable**, never zero. Partial or stale prices and balances remain visibly marked; an explicitly reported zero remains zero. Failed SDK lookups are recorded by exception type only, without exposing credentials.
- An explicitly reported allowance and consumed amount can be shown separately when the SDK does not provide a verified AI-credit balance. A current SDK-backed AI-credit quota takes precedence if a future adapter can return one.
- The deterministic token estimator, budget decisions, original Codex ingestion, cumulative session deduplication and optional AI reviewer consent boundaries remain unchanged.

## Verification

- **17/17 .NET 10 projects compiled in Release, zero warnings/errors; 169/169 MSTest cases passed** (22 added since v0.8).
- Offline tests cover permission denial, unknown/partial/stale prices, dynamic price changes, independent SDK failures, account-reported balances, explicit zero, provider/account/unit separation, quota reset expiry, absent remaining values, and premium quota independence.
- There is **no authenticated user account available** in this environment, so the live SDK probe is compiled and its behavior is simulated with injected adapters. It has not been exercised against real account pricing or charges.

## How to use

```powershell
dotnet run --project .\src\TokenBudget.Cli -- copilot-probe `
  --account pjotr-personal --model your-copilot-model-id `
  --allow-live-account-lookup true --allowance 30000 --used 9000
```

The `30,000` credits/month figure is an account-reported example, not a universal GitHub Copilot plan default. `--allow-live-account-lookup` must be explicitly true. No model tariff is hardcoded. The SDK may report only premium-interaction quota and **not** a verified AI-credit balance, which the report distinguishes.

## Next acceptance milestone

1. With explicit account consent, run `copilot-probe` in the actual authenticated Copilot environment and confirm the authenticated principal separately from the user-provided label.
2. Compare returned model prices and quota units with the GitHub account billing UI, including reset dates, account plan mode, model identity and metered versus legacy charging. Do not convert SDK nano-AIU data into verified billed credits until the conversion is independently confirmed.
3. Add optional, per-account quota provenance snapshots and drift detection **without** storing secrets or silently replacing user-reported balances.
4. Gather completed execution evidence for the v0.8 calibration diagnostics. Any eventual calibration adjustment requires human review and cannot override the original deterministic budget gate.

Official API reference: [GitHub Copilot SDK usage and billing metrics](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/usage-and-billing).
