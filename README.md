# AgentForge / TokenBudget

A **read-only, provider-neutral .NET 10 token-budget preflight** for AI coding plans and agent workflows.

**Development status (v1.1 preview, November 2, 2026):** an easy-to-use `agentforge` .NET 10 tool, backed by provider-aware forecasting, account-isolated usage reconciliation, conservative calibration diagnostics, and a consent-gated Copilot SDK account probe. The VS Code Copilot custom-agent and MCP integration is implemented; Windows CI verified the installed tool's `initialize`, `tools/list`, and `preflight_task` call, with **17 Release projects, zero warnings and 191 passing tests**. First use inside an actual signed-in VS Code Copilot session and authenticated account billing verification remain pending. See [VS Code Copilot integration](docs/VSCODE_COPILOT.md), [v1.0 release notes](docs/RELEASE_v1.0.md), [provider-aware billing](docs/PROVIDER_BUDGETS.md), and [session reconciliation](docs/SESSION_RECONCILIATION.md). Forecasts remain advisory: no real completed-agent calibration dataset is available.

## Quick start — one task, no long argument list

Once you have the .NET 10 SDK and this source tree, run **one-time setup** in a terminal:

```powershell
dotnet run --project .\src\TokenBudget.Cli -- setup
```

The wizard asks for a provider, model name, account label, and optionally your account-reported monthly Copilot AI-credit allowance and used credits. It stores only non-secret preferences in your user application-data directory. To estimate a task:

```powershell
dotnet run --project .\src\TokenBudget.Cli -- "Refactor the lighting engine"
```

Or use a Markdown milestone plan (no ten-option invocation):

```powershell
dotnet run --project .\src\TokenBudget.Cli -- preflight .\examples\wyrmforge-g6-budget-plan.md
```

For the shortest commands, install the v1.0 preview NuGet **tool package supplied with the release** from the directory containing its `.nupkg` (not yet published to NuGet.org):

```powershell
dotnet tool install --global --add-source .\downloads AgentForge.TokenBudget.Cli --version 1.1.0-preview.1
agentforge setup
agentforge "Refactor the lighting engine"
agentforge preflight .\examples\wyrmforge-g6-budget-plan.md
agentforge
```

To use the same deterministic preflight inside **GitHub Copilot Chat in VS Code**, open the target project folder and run `agentforge vscode init` once. Select **AgentForge** from Copilot's agent picker and enter a single task like `Refactor the lighting engine`. This installs a read-only MCP-backed custom agent; it never auto-executes the task. See the [VS Code setup guide](docs/VSCODE_COPILOT.md) for installing the tool and verifying its first call.

The final command prompts you for a task interactively. `agentforge --help` lists advanced capabilities. You can also run `agentforge check "..."` as an alias for `preflight`. Installation requires a .NET 10 runtime; the packaged tool includes the Copilot SDK's large multi-platform runtime assets, so its download is substantially larger than the source-only project.

## GitHub repository and Pages

The public repository is **[pjotrcasteel/AgentForge](https://github.com/pjotrcasteel/AgentForge)**, with this .NET solution at its root. The repository includes:

- `.github/workflows/ci.yml` for Windows .NET 10 Release build and MSTest verification.
- `.github/workflows/pages.yml` for publishing the static website from `docs/` on `main`.
- `docs/index.html` for the responsive AgentForge introduction and CLI quick start.

The source is published. To activate the website for the first time, choose **GitHub Actions** as the Pages deployment source under [Settings → Pages](https://github.com/pjotrcasteel/AgentForge/settings/pages). The Pages workflow then publishes the site on changes to `docs/`. The initial Pages run failed because the repository did not yet have Pages enabled; after changing the setting, rerun the Pages workflow or push a documentation update. The site describes the estimator; it does **not** run estimation in the browser or expose the local API to the internet.

These workflow files do not grant AgentForge permission to access credentials or run provider-paid AI tasks. The main application remains offline-by-default.

By default, preflight **does not upload prompts, invoke a paid reviewer or execute an AI coding task**. The token forecast and prompt-quality guidance work offline. Exact provider costs stay **unknown** until valid model prices and billing details are available; absent or stale balances never mean zero credits. You can explicitly opt in to local Copilot SDK model-pricing lookup during setup, but that still does not submit the task content. The 30,000-credit example represents an account-reported allowance, not a universal plan default.

## What it currently does

- Parses Markdown milestone boundaries, explicit dependencies, file mentions, tests, visual proof and human approval gates.
- Inspects local repository **file metadata only**, skipping `.git`, `bin`, `obj`, `node_modules` and symlinks; no shell commands or builds during an estimate.
- Simulates 10,000 seeded scenarios for cumulative model tokens and **peak context occupancy (including output reserve)**.
- Separately reports input/output token scenarios and forecasts provider-specific account usage: Copilot AI credits, API USD and advisory subscription limits.
- Fetches Copilot per-model tariffs from the official `.NET` SDK `models.list` with explicit live lookup; never assumes one AI credit equals one token.
- Compares P90 provider-unit cost against account-reported remaining balance, per-task maximum and protected reserve, without merging provider accounts.
- Checks prompt scope, suggests dependency-safe staging and optionally generates an improved prompt retaining the original requirements verbatim.
- Applies budget gates with independent cumulative and peak-context caps and customizable risk percentile.
- Returns structured JSON or a Markdown report from a CLI; exposes synchronous `POST /v1/estimates` and `/v1/capabilities` in a local API.
- Uses an **offline heuristic** for submitted plan size by default; exact count of **submitted input text only** is available for OpenAI Responses and Anthropic Messages after explicit opt-in and a provider API key.
- Inspects recorded `codex exec --json` event streams, and optionally imports usage counters into a local SQLite database without storing raw transcripts. Cached and reasoning tokens are subsets and are not double-counted.
- Optionally reconciles cumulative Copilot/provider session checkpoints by provider, account and session ID. Identical replays add zero usage, growing snapshots add only the delta, and unverified AI-credit figures never count toward verified cost backtesting.
- Reports observed P10/P50/P90 forecast coverage and a conservative statistical lower bound for P90; flags underforecasting or sufficient evidence for **manual review**, without silently tuning estimates.
- Optionally probes live Copilot model prices and quota through the official SDK, keeping unknown AI-credit balances separate from premium-interaction counts and visibly labeling user-reported balances.

## Prerequisites

- .NET **10** SDK
- Windows PowerShell (examples below), macOS or Linux
- Internet/NuGet access for restoring MSTest packages (not needed for the core library)

## Code organization

The engine is reusable without hosting the API, running the CLI or enabling AI review:

| Project | Responsibility |
| --- | --- |
| `TokenBudget.Domain` | Provider targets, plan/forecast contracts, interfaces |
| `TokenBudget.Application` | Plan parsing, repository inspection, token counting, forecasting, policy |
| `TokenBudget.Provider.Codex` | Read-only Codex JSONL event parsing |
| `TokenBudget.Billing` | Provider-neutral cost contracts, decimal accounting, independent account budgets, prompt and model alternatives |
| `TokenBudget.Provider.Copilot` | Official GitHub Copilot SDK dynamic model catalog, quota, session usage and event adapters |
| `TokenBudget.Telemetry` | Opt-in local SQLite history and calibration diagnostics |
| `TokenBudget.Agent` | Optional read-only AI review, isolated from the deterministic decision |
| `TokenBudget.Cli` | Thin entry point, simple `setup`/`preflight` commands, non-secret user preferences in `Configuration/`, advanced handlers in `Commands/` |
| `TokenBudget.Api` | Thin entry point; route handlers in `Endpoints/`, security in `Security/` |
| `TokenBudget.Mcp` | Read-only stdio MCP integration |
| `TokenBudget.Agent.Cli` | Separate opt-in agent-review executable |
| `tests/*` | MSTest regression projects, including dedicated CLI parsing/alias tests |

Existing v0.5 request/response fields and CLI commands remain backward compatible; v0.6 added an optional `tokenCategories` forecast field and a `budget` command. v0.7 added `ingest-provider-session` and `provider-history` without changing the original Codex import format. v0.8 added read-only `provider-calibration`; v0.9 adds `copilot-probe`; v1.0 adds interactive `setup` and one-task `preflight`. The CLI
uses one strict parser for `--name value` options; unknown, duplicate or missing options fail
early. API routes now include `/v1/provider-budgets` alongside `/health`, `/v1/capabilities`, `/v1/estimates`, and
`/v1/agent/preflight`. See [development conventions](docs/DEVELOPMENT.md) before adding
another host or provider.

## Build and test (PowerShell)

```powershell
cd .\TokenBudget
dotnet restore .\TokenBudget.slnx
dotnet build .\TokenBudget.slnx -c Release --no-restore -m:1
dotnet test .\TokenBudget.slnx -c Release --no-build -m:1
```

## Preflight a Markdown handover

### Provider-aware Copilot budget (new)

```powershell
dotnet run --project .\src\TokenBudget.Cli -- budget .\examples\wyrmforge-g6-budget-plan.md `
  --provider copilot --model your-copilot-model-id --account pjotr-personal `
  --allowance 30000 --used 9000 `
  --price-input 0.75 --price-output 4 --price-batch 1000 `
  --max-total-tokens 250000 --reserve-units 1000 --improve-prompt true
```

**The example token rates are invented for demonstrating the CLI, not GitHub prices.** `30,000` credits is a configured *user-reported* monthly entitlement, not a default. For live catalog pricing, remove the three manual price arguments and add `--live-copilot true` after authenticating the SDK. This does not send your prompt to Copilot; the task is never executed. The SDK's `premium_interactions` quota is **not** a verified AI-credit balance; retain explicitly reported account balances if credit quota is unavailable. More detail: [provider budgeting](docs/PROVIDER_BUDGETS.md).

### Explicit Copilot billing capability probe (v0.9)

Run this only after the Copilot SDK/CLI has been authenticated in the intended environment. This command reads the model catalog and account quota; it **does not send a prompt, create an agent session or execute a task**:

```powershell
dotnet run --project .\src\TokenBudget.Cli -- copilot-probe `
  --account pjotr-personal --model your-copilot-model-id `
  --allow-live-account-lookup true --allowance 30000 --used 9000
```

The `30,000`/`9,000` values remain **your explicitly account-reported figures**, not GitHub plan defaults. Without `--allow-live-account-lookup true`, no SDK account query is attempted. The JSON report independently labels `modelPriceState`, `creditBalanceState` and `premiumQuotaState` as available, partial, account-reported, unavailable or failed. The account label is **not verified as the authenticated Copilot identity** by this diagnostic; use a separate account verification before trusting a live balance. If `account.getQuota` only exposes premium interactions, those are **not converted into AI credits**. The probe does not store credentials or billing results.

### Original token-only analysis

```powershell
dotnet run --project .\src\TokenBudget.Cli -- analyze .\examples\arena-plan.md `
  --provider codex `
  --model gpt-6 `
  --repo . `
  --max-total-tokens 250000 `
  --max-peak-context-tokens 120000 `
  --format markdown `
  --output .\tokenbudget-report.md
```

Use your actual Codex model ID in `--model`; do not assume a ChatGPT model label corresponds to an API model identifier. The example `--repo .` inspects TokenBudget itself, not WyrmForge. Point it at the relevant checkout for useful repo evidence.

Exit status: `0` pass, `2` warning, `3` reject, `4` insufficient evidence, `1` execution/validation error. **A pass is a prediction, not permission to execute or bypass approvals.** Without empirical calibration the engine normally returns `Warning` or `Reject`, not a fabricated high-confidence pass.

Read capabilities:

```powershell
dotnet run --project .\src\TokenBudget.Cli -- capabilities
```

For direct-provider input counting (sends submitted plan text to provider; default is OFF):

```powershell
$env:OPENAI_API_KEY = "<your-key>"
dotnet run --project .\src\TokenBudget.Cli -- analyze .\examples\arena-plan.md `
  --provider openai --model <actual-api-model-id> `
  --allow-provider-upload true --max-total-tokens 250000
```

For Anthropic, set `ANTHROPIC_API_KEY`, use `--provider anthropic` and an Anthropic model ID. This counts the submitted plan payload only, not future agent context or tool traffic. Never pass secrets as part of the plan.

Inspect real Codex turn usage in recorded JSONL:

```powershell
# Capture JSONL yourself while running Codex, then inspect it separately.
codex exec --json "Inspect repository" > .\codex-run.jsonl
dotnet run --project .\src\TokenBudget.Cli -- inspect-codex .\codex-run.jsonl
```

Inspection alone does not save anything. Failed/truncated/malformed-completion streams are marked incomplete.

## Persistent Codex usage history (opt-in)

For a single run, produce a JSON forecast report, capture Codex output, and import the counters:

```powershell
# Use a real, supported model identifier and point --repo at the intended checkout.
dotnet run --project .\src\TokenBudget.Cli -- analyze .\examples\arena-plan.md `
  --model <codex-model-id> --provider codex --max-total-tokens 250000 `
  --format json --output .\forecast.json

# Capture a genuine Codex run separately (TokenBudget does not execute it).
codex exec --json "Your bounded task" > .\codex-run.jsonl

dotnet run --project .\src\TokenBudget.Cli -- ingest-codex .\codex-run.jsonl `
  --db .\local-data\usage.db --profile WyrmForge-Arena --model <codex-model-id> `
  --estimate-json .\forecast.json

dotnet run --project .\src\TokenBudget.Cli -- history `
  --db .\local-data\usage.db --profile WyrmForge-Arena --model <codex-model-id>
```

The importer supports UTF-8 and BOM-marked UTF-16 redirections from older Windows PowerShell, while rejecting invalid text. The `ingest-codex` command checks the recording for complete Codex turns and persists only metadata and counters. It uses the Codex thread ID as the default import identity (or a recording hash if no thread ID is present); `--run-id` can override it. A changed transcript from an already-ingested thread is deliberately rejected rather than double-counted. If a session legitimately spans multiple distinct tasks, specify separate run IDs and non-overlapping recordings. Importing the same run twice does not duplicate metrics. Importing different content under an existing run identity fails. Incomplete runs may be retained for auditing, but never contribute to calibration.

When an accompanying estimate is supplied, the history query reports the median *actual tokens / predicted P50* ratio and the observed coverage of the predicted P90. At least 20 matched complete recordings are required before it describes the data as potentially suitable for calibration. **No automatic scaling or confidence upgrade is applied in v0.5.** The matching forecast must correspond to the actual run scope, not merely to a similarly named project; input accuracy still depends on operator discipline. Real model input and output token counts are cumulative and the cache/reasoning fields are subsets.

The local history file can disclose model use and project profile names; keep it private and do not commit it to Git.

## Provider session reconciliation (v0.7, opt-in)

The official Copilot SDK exposes cumulative `session.usage.getMetrics` observations and individual `assistant.usage` events. **Do not add those two streams together.** The separate v0.7 ledger accepts cumulative checkpoints only, prevents duplicate counting and compares completed sessions with explicitly matched estimates. It never uploads plans or captures transcripts.

```powershell
# Synthetic example: the eight reported credits are NOT verified Copilot charges.
dotnet run --project .\src\TokenBudget.Cli -- ingest-provider-session `
  .\examples\copilot-session.fixture.json --db .\local-data\provider-sessions.db

# Repeating the same import returns "Duplicate" and zero usage delta.
dotnet run --project .\src\TokenBudget.Cli -- ingest-provider-session `
  .\examples\copilot-session.fixture.json --db .\local-data\provider-sessions.db

dotnet run --project .\src\TokenBudget.Cli -- provider-history `
  --provider copilot --account pjotr-personal --model configured-copilot-model `
  --db .\local-data\provider-sessions.db
```

The sample matches 1,000 actual tokens against a synthetic P50 of 1,000, so the displayed actual/P50 ratio is `1.0`. Its `creditsVerified: false` means **cost coverage is unknown**, despite the illustrative eight-credit field. Confidence gating requires at least 20 comparable completed sessions; this release reports diagnostics only and does not automatically modify forecasts. The SDK collector itself uses account-scoped session resolution and labels experimental nano-AIU conversions provisional. [Full usage and limitations](docs/SESSION_RECONCILIATION.md).

### Calibration diagnostics (v0.8)

```powershell
dotnet run --project .\src\TokenBudget.Cli -- provider-calibration `
  --provider copilot --account pjotr-personal --model configured-copilot-model `
  --db .\local-data\provider-sessions.db
```

The output separately reports P10/P50/P90 token coverage, the **95% Wilson lower bound** for observed P90 coverage, and verified Copilot-credit coverage when eligible. The v0.8 diagnostic requires **at least 100 distinct, completed, explicitly matched runs**, plus a P90 lower bound of at least 90%, before marking a dataset as a *candidate for manual review*. Twenty completed runs can already flag concerning underprediction, but cannot establish reliability. The previous v0.7 history threshold of 20 is retained for compatibility and is not an authorization to calibrate. No automatic model-price, token-forecast or deterministic policy change is applied. See [v0.8 release notes](docs/RELEASE_v0.8.md).

## v0.3 — Codex MCP server (stdio, offline only)

Build the solution, then register the MCP server with Codex on Windows:

```powershell
$repo = (Resolve-Path .).Path
# Optional: allow read-only repository inspection only within this root.
$env:TOKENBUDGET_REPO_ROOT = $repo
codex mcp add tokenbudget --env TOKENBUDGET_REPO_ROOT=$repo -- dotnet run `
  --project "$repo\src\TokenBudget.Mcp\TokenBudget.Mcp.csproj" --no-build
codex mcp list
```

`TokenBudget.Mcp` has two read-only tools:

- `preflight(plan, modelId, target, maxCumulativeTokens, maxPeakContextTokens?, repositoryPath?)`: returns the deterministic estimate as JSON. The plan has a 64,000-character limit, and the tool **never** grants external upload permission or executes a plan.
- `capabilities()`: describes supported targets and current integration limitations.

The server does not permit arbitrary filesystem inspection: repository paths are disabled unless `TOKENBUDGET_REPO_ROOT` was provided to the server, and paths outside that directory and symlinked paths are rejected. The tools themselves are offline and read-only; the surrounding Codex client may still carry your submitted plan in its own model context, independently of TokenBudget.

For ChatGPT Work on the web, a local stdio server is **not** an externally reachable plugin. A separately deployed, authenticated HTTPS MCP service is still required before Work can call it directly. Do not publicly expose the development HTTP API.


## HTTP API (local)

```powershell
dotnet run --project .\src\TokenBudget.Api --urls http://127.0.0.1:5072
```

`POST http://127.0.0.1:5072/v1/estimates` accepts a JSON `AnalyzePlanRequest`:

```json
{
  "plan": "## M0 - Build\nImplement the estimator and tests.",
  "modelId": "configured-model-id",
  "target": "CodexCli",
  "budget": {
    "maxCumulativeTokens": 250000,
    "maxPeakContextTokens": 120000,
    "riskTolerance": 0.1
  },
  "randomSeed": 1729,
  "simulationCount": 10000
}
```

`GET /health` and `GET /v1/capabilities` are available. Access to `/v1/*` is loopback-only unless you set `TOKENBUDGET_API_KEY`; then send `X-TokenBudget-Key`. For production, deploy behind TLS, proper authentication and request-size limits; don't expose this initial API directly to the Internet.

## Forecast assumptions and limits

- Plan-size counting defaults to a text-length heuristic; **not** an exact model tokenizer.
- Provider counting APIs count the payload TokenBudget actually sends, not prompts/tool schemas that Codex or Work may assemble internally.
- Seeded Monte Carlo numbers are **uncalibrated workload scenarios**, not validated P90 coverage guarantees.
- The original token-only analyzer cannot evaluate `MaxApiCostUsd` by itself. Use the new provider budget layer for USD or Copilot credits; `MaxApiCostUsd` remains a legacy advisory setting in the old command.
- `ChatGptWork` uses external reports as preflight; TokenBudget does not claim to read Work's internal tokens or intercept/stop its execution.
- The model cannot guarantee a future context window limit unless you specify the verified model-specific value with `--model-context-limit`.
- The Markdown parser is intentionally conservative and supports explicit headings like `## M0 - Foundation`. More sophisticated requirement mapping and segmentation are future work.
- The CLI report may be written if you explicitly supply `--output`; estimation itself never modifies repository files.

## Next milestones

See the [current roadmap](docs/IMPLEMENTATION_STATUS.md). The next priority is authenticated live Copilot SDK acceptance and verified tariff/credit provenance; then gather actual completed runs to test calibration diagnostics against reality. Later work includes external OpenAI/Anthropic pricing catalogs and an authenticated remote MCP service.

### Earlier v0.5 milestone history

1. **M1 acceptance complete:** the .NET 10 Release build, full MSTest suite and local integrations are verified; keep CI green.
2. **M2:** semantic C# dependency analysis and bounded evidence of likely file impact.
3. **M3:** accept SQLite Codex JSONL ingestion as a development slice; next implement app-server cumulative snapshot reconciliation and OpenAI Agents telemetry.
4. **M4:** validate local MCP stdio handshake and tool invocation; later implement a properly authenticated remote MCP server for Work.
5. **M5/M6:** use the SQLite history for backtesting and verified calibration (not just summary ratios), then add the dependency-safe optimizer and acceptance runs.

Original full architecture: `docs/TokenBudget_Agent_Architecture_v1.md`.

## Agent mode (v0.2 development slice)

A new **Microsoft Agent Framework** integration is isolated in `TokenBudget.Agent` and `TokenBudget.Agent.Cli`.
The original `TokenBudget.Cli` remains fully offline by default. There is no requirement to have an AI API key to get a forecast.

**Offline agent preflight:**

```powershell
dotnet run --project .\src\TokenBudget.Agent.Cli -- preflight .\examples\arena-plan.md `
  --target codex --model <your-codex-model-id> --budget 250000 --repo .
```

**Optional LLM risk review** (requires an OpenAI API key and explicit permission to send the plan text):

```powershell
$env:OPENAI_API_KEY = "<your-api-key>"
dotnet run --project .\src\TokenBudget.Agent.Cli -- preflight .\examples\arena-plan.md `
  --target codex --model <your-codex-model-id> --budget 250000 --repo . `
  --ai-model <your-openai-api-model-id> --allow-ai-upload true `
  --output .\tokenbudget-agent-report.md
```

The Microsoft Agent Framework reviewer has **only two read-only function tools**: `GetBudgetEvidence` and
`GetMilestoneDependencies`. It must call both tools. The AI narrative is kept in a visibly **non-authoritative**
section and cannot alter any deterministic result or human approval gate. It has **no shell, Git, file write, internet
search, or agent execution tools**. It does not retrieve or upload repository source content; the submitted plan text
*is* sent to OpenAI when enabled, up to 40,000 characters for the review. A partial-plan warning is included when
that limit is reached. Keep secrets and proprietary content out of submitted plans unless you have permission to
transmit them. AI review incurs **additional API token usage, not included in the projected execution budget**.

A caller who provides `--ai-model` without `--allow-ai-upload true` gets an error **before uploading anything**.

### HTTP agent preflight (opt-in)

This endpoint is disabled unless the server explicitly enables it **and** configures an API key:

```powershell
$env:TOKENBUDGET_ENABLE_AI_REVIEW = "true"
$env:TOKENBUDGET_API_KEY = "<long-random-secret>"
$env:OPENAI_API_KEY = "<your-api-key>"
dotnet run --project .\src\TokenBudget.Api --urls http://127.0.0.1:5072
```

`POST /v1/agent/preflight` uses the existing `X-TokenBudget-Key` header and accepts:

```json
{
  "estimate": {
    "plan": "## M0 - Build\nCreate contracts and tests.",
    "modelId": "your-execution-model-id",
    "target": "CodexCli",
    "budget": { "maxCumulativeTokens": 250000 }
  },
  "reviewModelId": "your-openai-api-model-id",
  "allowAiUpload": true
}
```

This is meant for localhost or a separately authenticated TLS reverse proxy; **do not expose this development
endpoint publicly**. The flag is server-wide consent, and each request must separately opt in. MCP, structured
subagent workflows and persistent calibration are still on the roadmap.

### Verification notice

Historical note: v0.2 was distributed before a build could be run. **The v0.4 source has now been compiled and tested** on .NET 10.0.400. The OpenAI Responses client remains marked as experimental by its SDK; only that optional construction call has a scoped `OPENAI001` suppression.

## v0.4 validation evidence

- The complete solution restored and built with .NET SDK 10.0.400, Release, single-process MSBuild: **0 warnings, 0 errors**.
- All **54 test cases passed** in four MSTest assemblies.
- A live MCP client successfully performed `initialize`, `tools/list`, and `tools/call` (`preflight`) with valid JSON-RPC and no extraneous stdout.
- A live HTTP client verified normal estimates (HTTP 200), an outside-root repository rejection (HTTP 403), and allowlisted repository inspection (HTTP 200).
- A synthetic Codex recording was imported twice: first `Inserted=true`, then `Inserted=false`; history contains one matched run and correctly refuses calibration.
- The HTTP and MCP repository path validation now share the same `RepositoryPathPolicy`. API callers cannot select arbitrary directories without an explicitly configured `TOKENBUDGET_REPO_ROOT`.

## Remaining acceptance gates

- Import a **real** Codex JSONL recording twice and confirm only one record is stored. Import a truncated/failed recording and confirm it cannot calibrate.
- Compare Codex CLI counters to the database and verify cached/reasoning subsets are not added to totals.
- Pair at least one true execution with a forecast generated **before** the run and inspect the ratio. Do not assume the projection was accurate.
- Run a genuine opt-in API-model review; account for its additional token usage separately from the forecasted coding task.
- Run `dotnet format` on a normal developer machine or CI. In this environment, its build-host named-pipe connection is blocked even though the solution builds and tests successfully.

The current validation confirms **local** runtime behavior; it does not prove the accuracy of future-token predictions or remote service integration.
