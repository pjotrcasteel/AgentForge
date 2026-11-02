# TokenBudget v0.4 — Verified .NET 10 release candidate

Date: November 2, 2026  
Runtime: .NET SDK 10.0.400 / net10.0

## Delivered

- Restored and built all 13 projects in Release with warnings treated as errors.
- Resolved the pinned OpenAI SDK's `OPENAI001` diagnostic with a narrowly scoped suppression around the optional Responses-client construction.
- Added `RepositoryPathPolicy` and applied it consistently to HTTP and MCP entry points.
- Hardened Kestrel's request-body limit in addition to the application-level check.
- Added five repository path policy tests.
- Added an intentionally synthetic Codex JSONL fixture for repeatable CLI/SQLite checks.

## Verification performed

| Check | Result |
| --- | --- |
| Solution restore | Pass — all projects |
| Release build | Pass — 13 projects, zero warnings, zero errors |
| MSTest | Pass — 54 passed, 0 failed, 0 skipped |
| MCP stdio | Pass — initialize, tools/list, preflight tools/call |
| HTTP local API | Pass — estimate HTTP 200; out-of-root repository HTTP 403; allowlisted repository HTTP 200 |
| CLI + SQLite import | Pass — fixture inserted once and duplicate ignored |
| History and calibration policy | Pass — one matched synthetic run is insufficient for calibration |
| `dotnet format whitespace` | Not verified — local build-host named-pipe restriction |
| External OpenAI/Anthropic calls | Not verified — live provider credentials not used |
| Actual Codex run telemetry | Not verified — only synthetic fixture used |

## Important limits

The Monte Carlo scenarios are **not statistically calibrated** and must be treated as advisory. Input token counts default to a heuristic unless the user explicitly opts into supported provider counting. ChatGPT Work internal token usage is not accessible here. The MCP server is a local stdio process, not a hosted Work integration.

## Next release (v0.5)

1. Collect complete real Codex recordings matched to pre-run forecasts.
2. Normalize and reconcile Codex app-server cumulative usage snapshots.
3. Add calibration backtesting and coverage reporting without promoting confidence prematurely.
4. Implement authenticated remote MCP only when its deployment and access model are chosen.

## Reproduce the .NET verification on Windows

```powershell
dotnet restore TokenBudget.slnx --disable-parallel -m:1
dotnet build TokenBudget.slnx -c Release --no-restore -m:1
dotnet test TokenBudget.slnx -c Release --no-build -m:1
dotnet format whitespace TokenBudget.slnx --verify-no-changes --no-restore
```
