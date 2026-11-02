# TokenBudget .NET 10 — v0.5 organization and regression release

Date: 2026-11-02

## Outcome

The existing read-only token-budget estimation engine and optional agent review now have
cleaner source-code boundaries. This is a **source-organization release**, not a claim of
better predictive accuracy or new live-provider coverage. Previous v0.4 functionality and
public JSON routes/fields are preserved.

## What changed

- Split the former monolithic domain `Contracts.cs` into `ExecutionTargets.cs`,
  `PlanContracts.cs`, `ForecastContracts.cs` and `Ports.cs`. Public interfaces, records,
  enum members and constructor shapes remain the same; XML summaries describe their purpose.
- Refactored `TokenBudget.Cli/Program.cs` into a three-line entry point, `CliApplication`,
  independent `Commands/AnalyzeCommand.cs`, `CodexInspectionCommand.cs`,
  `TelemetryCommand.cs`, `CliOptions.cs`, and `ConsoleJson.cs`.
- Refactored `TokenBudget.Api/Program.cs` into composition only. Routes now live in
  `Endpoints/`; request-size, local-only/API-key authorization and explicit AI-review opt-in
  live together in `Security/TokenBudgetSecurityMiddleware.cs`.
- Added `TokenBudget.Cli.Tests` to the solution: 15 regression cases for strict option
  parsing, provider aliases and invariant numeric values.
- Added `docs/DEVELOPMENT.md` and a repeatable Linux/macOS `scripts/smoke-api.sh`.
- Normalized changed C# files to UTF-8 with BOM, CRLF and the project editor configuration.

## Verified acceptance

Runtime: **.NET SDK 10.0.400**.

| Check | Result |
| --- | --- |
| Release solution build | **14/14 projects**, 0 warnings, 0 errors |
| MSTest full suite | **69 passed**, 0 failed, 0 skipped |
| CLI help and capabilities | Executed successfully |
| Offline `analyze` JSON | Produced valid P10/P50/P90 and `Reject` with exit 3 for a 250,000-token budget |
| Codex fixture inspection | Complete run, 6,300 total input/output tokens |
| SQLite telemetry | First import inserted; identical import deduplicated; history returned one complete run and insufficient calibration evidence |
| Local HTTP API | Six checks passed: health 200, unauthorized 401, authorized capabilities 200, estimate 200, outside-root repo 403, review disabled 403 |
| Source formatting | Verified BOM + CRLF on new C# code; no lines over 180 characters |

Run from the project root:

    dotnet restore TokenBudget.slnx
    dotnet build TokenBudget.slnx -c Release --no-restore -m:1
    dotnet test TokenBudget.slnx -c Release --no-build -m:1

Optional Linux/macOS HTTP smoke after the Release build:

    bash scripts/smoke-api.sh

The API script binds only to loopback and uses a synthetic test key. It starts and
terminates its own server. On Windows, use the PowerShell HTTP commands in the README
or invoke the same API routes with your preferred HTTP client.

## Known limitations and next work

- Monte Carlo scenarios remain uncalibrated; 69 green tests do not establish true P90
  coverage, future context-window usage or provider billing costs.
- The default initial plan count remains heuristic. Provider-assisted counts are explicit
  opt-in, and count only submitted input text.
- No live OpenAI reviewer, Anthropic counter, Codex app-server token stream or remote
  ChatGPT Work connector was exercised. Those capabilities must not be marked complete.
- Next milestone: app-server cumulative snapshot reconciliation, with complete/partial
  run evidence and idempotent storage, followed by matched-run backtesting before any
  forecast calibration or confidence upgrade.
