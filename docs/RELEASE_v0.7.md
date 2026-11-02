# AgentForge / TokenBudget v0.7 — Provider session reconciliation

**Date:** 2026-11-02  
**Scope:** A conservative, optional cumulative-usage ledger layered onto the existing provider-neutral .NET 10 engine.

## Implemented

- Cumulative provider session snapshots with account/provider/session isolation and **transactional SQLite upserts**. Counters are monotonic; replayed snapshots do not double-count input, output or credits.
- Validation prevents per-call `assistant.usage` from entering the cumulative ledger. Historical Codex JSONL run ingestion and existing deduplication are unchanged.
- Explicit binding of one **completed, single-model** session to the matching token/cost forecast. Report median actual/P50 tokens and observed P90 token coverage.
- A separate **verified** AI-credit coverage metric: experimental SDK nano-AIU figures remain provisional, and unknown cost coverage remains null.
- Defensive handling of optional/missing counters, stale snapshots, finalized sessions, model identity changes, post-completion token mutation, account crossings and cost evidence changes.
- Copilot SDK collector account-scoped session resolution, model IDs in observations and differentiation of cumulative metrics versus per-call events.
- CLI `ingest-provider-session` and `provider-history` for explicit, local imports and evidence reports. Includes a labeled synthetic JSON fixture.
- New history comparison threshold: 20 completed matched sessions before data is considered suitable **to consider** calibration. No automatic tuning or confidence upgrade.
- Documentation: [`SESSION_RECONCILIATION.md`](SESSION_RECONCILIATION.md), updated README and milestone tracker.

## Verification

- .NET **10** Release solution with **17 projects**.
- **126/126 MSTest tests passed** (100 v0.6 + 26 new).
- **Zero compiler warnings/errors**, with warnings treated as errors. Includes new tests covering idempotence, counters, account isolation, cost verification, finalization, stale updates and CLI entry points.
- Synthetic CLI import was performed twice: first `Inserted` with an 800-input/200-output/eight-provisional-credit delta; second `Duplicate` with zero delta. Historical query reported one matched completed run, actual/P50 = 1, P90 token coverage = 1, no verified cost coverage and calibration threshold not met.

The eight-credit fixture and all forecast prices are **demonstration values, not real Copilot tariffs or an authenticated billing charge**. No live Copilot account, provider API tokens or user credentials were accessed or stored.

## Remaining limitations and next milestone

1. Run explicitly consented authenticated Copilot SDK acceptance against a test account to establish which pricing, quota, session metrics and billed AI-credit observations are available **in practice**, and inspect billing-mode behavior.
2. Add verified adapter-to-ledger ingestion (without persisting secrets), price snapshot provenance and updated quota reset observations. Preserve missing/unavailable states rather than assuming zero.
3. Gather at least 20 comparable real executions per provider/account/model; evaluate bias and P90 coverage before proposing bounded calibration, with human approval for any forecast policy change.
4. Later: external OpenAI/Anthropic dynamic prices, more provider usage collectors, an authenticated remote MCP integration and safe dependency-aware prompt optimization.

See [`PROVIDER_BUDGETS.md`](PROVIDER_BUDGETS.md) for the provider-specific cost semantics and [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md) for milestone tracking.
