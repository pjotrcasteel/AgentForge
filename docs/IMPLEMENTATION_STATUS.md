# Implementation status — 2026-11-02 (v1.0 preview verified locally)

## GitHub publication — 2026-11-02

- Added a static AgentForge landing page at `docs/index.html`, with explicit uncalibrated-forecast and billing-unknown notices.
- Added `.github/workflows/pages.yml` for GitHub Pages from the `docs/` directory on `main`; retained the existing .NET 10 CI workflow.
- The public [`pjotrcasteel/AgentForge`](https://github.com/pjotrcasteel/AgentForge) repository now exists, with the complete v1.0-preview source on `main` (initial source commit `c745965`). GitHub verified the remote README, source project, tests and Pages workflow.
- The initial Pages workflow failed because Pages has not yet been enabled under repository **Settings → Pages**. Choose **GitHub Actions** there, then rerun the Pages workflow or push a documentation update. The website is **not yet claimed to be live**.
- A GitHub Actions `.NET 10` validation run was triggered by the source publication; its result must be checked separately before calling the newly published revision CI-verified.
- This documentation/website addition has not been rebuilt or retested in the current environment, which lacks the .NET 10 SDK. The 17-project/183-test figures below refer to the previously verified v1.0 source release.

## Provider-aware budgeting roadmap (v0.6–v1.1)

| Workstream | Priority | Planned acceptance | Status |
| --- | --- | --- | --- |
| v0.6 Provider-neutral forecasting | P0 | Preserve token forecast and expose separately modeled input/output categories | Implemented, tested |
| v0.6 Budget contracts and policy | P0 | Isolate account/provider balances; compute credits/USD with explicit source and unknown-state semantics; enforce P90 and task reserves | Implemented, tested |
| v0.6 Copilot foundation | P0 | SDK-derived pricing/quota/usage integration where supported; account-reported 30,000 allowance is opt-in configuration, never a default | SDK integration compiled; live account acceptance pending |
| v0.6 CLI, quality guidance, tests | P0 | Budget command, explicit manual fallback, scope advice, consent boundaries, null/stale/legacy/rounding/billing-mode MSTest cases | Implemented, tested |
| v0.7 Provider breadth | P1 | OpenAI/Anthropic metered adapters; Codex/ChatGPT advisory subscriptions with unknown usage clearly stated | Configured-price support present; live feeds pending |
| v0.7 Session reconciliation | P1 | Account-scoped cumulative snapshots, monotonic deltas, no per-call double count, confidence-gated actual-vs-forecast history | Implemented, tested with synthetic data |
| v0.7 Live integration | P1 | Authenticated SDK integration against consenting test account; pricing drift and quota reset checks; API endpoint and stable schema | Blocked on live test account |
| v0.8 Calibration diagnostics | P1 | Compute observed P10/P50/P90 coverage and conservative lower bound; warn on underforecast; manual review gate; preserve deterministic policy | Implemented and tested with synthetic data |
| v0.9 Copilot account probe | P1 | Explicitly consented SDK price/quota lookup with independent partial states and user-reported credit fallback; no prompts or sessions | Implemented and tested offline; authenticated acceptance pending |
| v1.0 Easy one-task CLI | P0 | `agentforge setup` once, then `agentforge "task"` or `agentforge preflight plan.md`; .NET tool install; preserve advanced CLI and consent/unknown states | Implemented; offline tool smoke verified |
| v1.1 Ease-of-install and actual account acceptance | P1 | Slimmer/official tool distribution; authenticated Copilot principal verification; safe pricing and quota reset checks | Planned; account approval required |
| v0.10 Controlled calibration | P2 | Real-run quality checks and separately approved statistical adjustment, only after authenticated provider acceptance | Planned; live evidence required |

The v0.5 baseline was reverified before edits on 2026-11-02: **14/14 projects built in Release, zero warnings/errors, 69/69 MSTest cases passed**. No live Copilot account or SDK credentials have been supplied. Token usage, model prices, provider account quota, USD charges, and AI credits are distinct quantities; missing quota data does not mean zero balance. An optional AI reviewer cannot override deterministic policy or consent gates.

## v1.0 preview — low-friction preflight (2026-11-02)

- **17/17 Release projects, zero compiler warnings, 183/183 MSTest cases passed**, including 14 new easy-CLI and settings tests since v0.9.
- `agentforge` with no arguments asks for a prompt. A single plain-language argument also runs the preflight. `agentforge preflight plan.md` and `agentforge check "task"` remain available.
- `agentforge setup` asks a few one-time questions. Strongly typed, bounded, non-secret JSON preferences are stored under user application data; `AGENTFORGE_CONFIG_PATH` supports an explicit test/automation override.
- Offline by default: token P10/P50/P90, scope advice, improved prompt, independent account-reported balance, and **unknown** prices when not observed. Only explicit saved consent permits SDK model pricing lookup; it never uploads the plan or authorizes execution.
- A locally packaged .NET tool was installed successfully. A smoke preflight of `Refactor the lighting engine` returned scope warning (exit 2), 21,010/36,382/55,807 estimated cumulative tokens and **unknown** AI-credit costs under intentionally unconfigured, offline defaults. These tokens are uncalibrated scenario outputs, not actual measured usage.
- GitHub Copilot SDK bundles large multi-platform assets into the package (~232 MiB). Tool distribution optimization and live account validation remain open work; no public NuGet publishing has been performed.
- See [v1.0 release notes](RELEASE_v1.0.md). Historical v0.9 and earlier milestones remain recorded below.

## v0.9 development — 2026-11-02

- **17/17 .NET 10 Release projects built with zero warnings/errors; 169/169 MSTest cases pass**, up from the v0.8 baseline of 147. No unit test accesses a real provider.
- New `copilot-probe` command requires explicit `--allow-live-account-lookup true`. It queries `models.list` and `account.getQuota` through the official SDK without submitting a prompt, creating a session or running an AI task.
- `CopilotBillingProbeService` uses injected, strongly typed SDK adapters and reports independent states for model pricing, GitHub AI-credit balances, and premium-interaction quotas. Missing/stale/partial results are not interpreted as zero.
- An explicitly account-reported 30,000-credit allowance is never treated as a universal plan default, or as verified by the SDK. The provided account label remains unverified against the signed-in SDK principal.
- Service and CLI tests cover permission denial, pricing changes, null/stale/expired quota, independent SDK failures, wrong account/currency, reported allowance fallback and explicit zero.
- **Live integration acceptance remains pending:** no authenticated Copilot account was accessed, no real model prices or credit balances were observed and no model task was executed.
- See [v0.9 release](RELEASE_v0.9.md).

## v0.8 development — 2026-11-02

- **17/17 .NET 10 Release projects built with zero warnings/errors; 147/147 MSTest cases passed**, up from the v0.7 baseline of 126. All new tests are offline.
- `ProviderCalibrationAnalyzer` computes actual P10/P50/P90 hit rates, median actual/P50 usage and conservative Wilson-score 95% lower bounds for P90 token forecasts, without changing any estimates.
- At 20 or more comparable complete executions, concerning underforecasting is flagged. At least 100 distinct matches **plus** a P90 lower bound of at least 90% are required for a *manual-review candidate*; no automatic calibration occurs.
- Verified GitHub AI-credit coverage is calculated separately from token coverage and from unverified nano-AIU. The CLI's `provider-calibration` command reads the existing metrics-only ledger for one exact provider/account/model.
- Authenticated live Copilot usage and a representative actual-versus-predicted dataset are **still unavailable**. Synthetic tests only verify the implementation's calculations and safeguards, not forecast accuracy.
- Local CLI smoke: importing the synthetic 800-input/200-output-token Copilot checkpoint returned `Inserted`; `provider-calibration` returned one comparable completed run, P90 hit rate `1.0`, 95% lower bound `0.20654931437723809`, `CollectMoreEvidence` and `null` verified-credit coverage. Read-only calibration does not create a missing database.
- See [v0.8 release](RELEASE_v0.8.md) for usage and acceptance limitations.

## v0.7 development verification — 2026-11-02

- **17/17 .NET 10 Release projects built with zero warnings/errors; 126/126 MSTest cases pass** (100 baseline v0.6 + 26 new regression/CLI cases).
- Added `SqliteProviderSessionStore` with a distinct account-scoped cumulative provider session checkpoint table and monotonic deltas. It rejects per-call counters, duplicate counting, post-completion token mutations, source switches and mixed-model single-model matches.
- Added explicit local `ingest-provider-session` / `provider-history` commands with a synthetic Copilot fixture. Initial replay yielded delta 800 input + 200 output + 8 *provisional* credits; identical replay yielded delta 0. Matching completed history reported token actual/P50 = 1 and observed P90 token coverage = 1, with cost coverage null because no credit evidence was verified.
- Account-scoped Copilot SDK resolver now distinguishes both account and session for usage collection. The experimental nano-AIU conversion remains marked provisional; **no live provider account was accessed**.
- A minimum of 20 completed, explicitly matched single-model runs is required before summaries say calibration could be considered. This release never automatically changes deterministic forecasts or budget policy.
- See [v0.7 release](RELEASE_v0.7.md) and [session reconciliation guide](SESSION_RECONCILIATION.md).

## v0.6 development verification — 2026-11-02

- Official GitHub Copilot .NET SDK 1.0.27 package restored and `models.list`, `account.getQuota`, `session.usage.getMetrics`, `assistant.usage` and experimental `session.quota` API calls compiled. No real account has been queried.
- **17/17 Release projects built with zero warnings/errors; 100/100 MSTest cases pass** (69 prior cases plus 31 new billing cases).
- Real local CLI smoke with explicitly configured synthetic rates and account-reported 30,000 / 9,000 credits produced 98.3705 / 166.0115 / 259.8890 credit P10/P50/P90, with 21,000 remaining before and 20,740.1110 after P90. Those *are computed outputs from the example rates*, not GitHub model prices.
- New local `POST /v1/provider-budgets` API route returned **HTTP 200** with a 4,030-byte structured response to a synthetic quota/pricing payload; existing loopback/API-key and repository allowlist boundaries remain enforced. Live account/deployment acceptance pending.
- See [provider billing guide](PROVIDER_BUDGETS.md) for pricing-source distinction, stable units and planned next work.

| Milestone | Status | Notes |
| --- | --- | --- |
| M0 Provider contracts | Compiled, unit-tested | Capability registry, provider input-count adapters, opt-in gating; no live provider API call yet |
| M1 Core estimation | Compiled, unit-tested | Domain, Markdown parser, seeded simulation, budget policy; full suite green |
| M2 Repository analysis | Partial | Bounded, metadata-only, read-only scan; no dependency graph |
| M3 Provider counting / telemetry | Partial | OpenAI/Anthropic count submitted input; offline Codex JSONL reader; v0.3 Codex and v0.7 provider-session opt-in SQLite ledgers; app-server & Agents usage pending |
| M4 CLI/API/MCP | Local integration verified; remote pending | CLI, localhost API and MCP stdio handshake/tool call verified; remote Work connector pending |
| M5 Optimization/calibration | Partial | v0.7 adds provider/account/model-scoped actual/P50 ratios and verified-only cost P90 coverage; zero real runs, no automatic calibration |
| M6 End-to-end acceptance | Local acceptance verified; real-provider acceptance pending | .NET 10 build/tests and local integrations verified; no real live model execution |

## v0.5 organization and regression verification — 2026-11-02

- Extracted domain contracts into dedicated plan, forecast, execution-target and port files.
- Extracted CLI command handlers under `Commands/`; one strict `CliOptions` parser supports all command options.
- Extracted HTTP routes under `Endpoints/` and bounded/authenticated middleware under `Security/`.
- Added a dedicated MSTest project for CLI option validation and provider-alias compatibility.
- See [v0.5 release notes](RELEASE_v0.5.md) for final verification commands, test count and smoke results.
- This is an organization and regression release: no calibration model or live provider capability is claimed.

## Verification evidence (historical v0.3 status, superseded by v0.4/v0.5)

- Files and project references: validated through a static structure check, not compilation.
- `dotnet build` / `dotnet test`: **not executed** (no SDK installed).
- External provider count requests: **not executed** (no credentials, no network).
- Codex JSONL fixtures: authored as MSTest tests, **not executed**.
- Git push/remote repository: **not attempted** (no destination repository specified).

Do not mark milestones accepted until a .NET 10 build and test run actually succeeds.

## v0.4 verified acceptance — 2026-11-02

- .NET SDK **10.0.400**; all **13 projects** compiled in Release with **0 warnings and 0 errors**.
- **54/54 tests passed** across the original tests, agent tests, MCP tests and telemetry tests.
- MCP stdio handshake: `initialize` succeeded; `tools/list` returned `capabilities` and `preflight`; `tools/call` returned valid forecast JSON.
- HTTP API smoke: no-repository estimate HTTP 200, allowed repository HTTP 200, path outside configured root HTTP 403.
- SQLite smoke: synthetic Codex JSONL import inserted once and deduplicated on replay; one matched run was correctly marked insufficient for calibration.
- Shared repository allowlist policy applied to HTTP and MCP. The HTTP API now limits request bodies at Kestrel as well as its application middleware.
- A single, targeted `OPENAI001` suppression is used for the optional experimental Responses-client construction; strict warnings-as-errors remains enabled.
- No live OpenAI/Anthropic review or real Codex recording was used. The local tests do **not** substantiate forecast P90 coverage or accuracy.
- `dotnet format whitespace` could not connect to its build-host pipe in this sandbox; verify formatting independently in CI/developer environment.

## v0.2 update — optional AI agent layer

- Added `TokenBudget.Agent`: a Microsoft Agent Framework (`Microsoft.Agents.AI.OpenAI` 1.26.0) reviewer that can call exactly two read-only C# tools on the completed estimate.
- Added `TokenBudget.Agent.Cli`: `preflight` mode; offline by default, explicit opt-in OpenAI review with separate review model ID.
- Added `TokenBudget.Agent.Tests`: fake reviewer contract/consent tests without live API calls.
- Added authenticated and disabled-by-default `POST /v1/agent/preflight` endpoint.
- AI advice cannot modify estimated token quantiles, confidence, decision, dependencies or human approval gates.
- This is the first *actual agent runtime integration*, but the real provider call, .NET compilation and test execution remain unverified in this environment.
- Next acceptance: run .NET 10 restore/build/test, execute one real opt-in review with a non-sensitive test plan, verify both tools invoked, and log provider consumption separately.

## v0.3 update — MCP and usage history

- New `TokenBudget.Mcp` .NET 10 stdio server using official `ModelContextProtocol` 2.2.0; exposes `Preflight` and `Capabilities` tools only. Repository inspections require a configured allowlisted root; provider uploads and execution are forbidden.
- New `TokenBudget.Telemetry` with local SQLite (`Microsoft.Data.Sqlite` 10.0.14), idempotent import keys, collision detection and metrics-only persistence. A complete recorded run can optionally be linked to a previous estimate by ID and model.
- New CLI commands: `ingest-codex` and `history`. Incomplete or malformed Codex event streams are retained separately but excluded from matched calibration statistics.
- Regression tests added for duplicate ingestion, conflicting IDs, incomplete streams, ratios, MCP path restrictions and offline preflight.
- No .NET SDK, NuGet access or real Codex event capture available in this environment. **MCP handshake, provider integration, compilation, and all tests are still unverified.**
- Next required gate: obtain green CI in a repository and a real MCP tool handshake before claiming M4 complete. No GitHub remote was changed.
