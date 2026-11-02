# AgentForge / TokenBudget v1.0.0-preview.1 — one-task preflight

**Date:** November 2, 2026  
**Goal:** Make daily budgeting as simple as asking AgentForge about one task, while leaving the provider-aware engine and advanced tools intact.

## Everyday usage

From the source folder (no installation needed):

```powershell
dotnet run --project .\src\TokenBudget.Cli -- setup
dotnet run --project .\src\TokenBudget.Cli -- "Refactor the lighting engine"
```

With the release's locally supplied `.nupkg` placed inside a `downloads` folder:

```powershell
dotnet tool install --global --add-source .\downloads AgentForge.TokenBudget.Cli --version 1.0.0-preview.1
agentforge setup
agentforge "Refactor the lighting engine"
agentforge preflight .\examples\wyrmforge-g6-budget-plan.md
agentforge                # Interactive prompt
agentforge --help
```

This package is **not published to NuGet.org**. A supported .NET 10 runtime must be installed. Existing advanced commands (including `budget`, `copilot-probe`, usage reconciliation and calibration) remain available in the CLI.

## What changed

- Introduced `Configuration/AgentForgeSettings` and `AgentForgeSettingsStore` with safe offline defaults and bounded user-scoped JSON; no API keys or authentication material are stored.
- Introduced `setup` for provider, model, account, user-reported Copilot AI-credit allowance/usage and optional live SDK model-pricing lookup consent.
- Introduced `preflight`, `check` and one-argument implicit preflight. Accepts plain-language task descriptions and existing Markdown plans, plus a no-argument interactive prompt.
- Existing deterministic engine produces token P10/P50/P90 and policy decisions. The prompt-scope advisor suggests staged work and a ready-to-use prompt that includes all original requirements verbatim.
- A vague broad change with no file boundary or testable acceptance checks receives an explicit scope warning. Unknown prices and quotas are reported as unknown, not zero or free.
- Packaged as `AgentForge.TokenBudget.Cli` .NET tool with command name `agentforge`.
- All preflight paths are read-only with `AllowProviderUpload: false`; no autonomous coding task runs, and no AI reviewer is invoked.

## Verification and caveats

- 17 .NET 10 Release projects compile with zero warnings and errors; 183 MSTest cases pass.
- A local `.nupkg` installs and exposes the `agentforge` host command. `Refactor the lighting engine` returned an expected scope warning (exit code 2) and uncalibrated 21,010 / 36,382 / 55,807 cumulative-token P10/P50/P90 forecast. With no configured model price or credit balance, GitHub AI-credit cost and remaining balance were both **unknown**.
- Since the official Copilot SDK ships multi-platform native CLI assets, this preview tool package is large (~232 MiB); slimmer distribution is a next milestone.
- The package has not been published to a public registry; install from the supplied local release package or run the project directly.
- No authenticated Copilot account, real model price or actual usage evidence was available. Any user-entered 30,000 credit allowance remains account-reported; GitHub premium requests and AI credits are not interchangeable.
- Token forecasts are not statistically calibrated against genuine execution workloads. Do not treat an advisory decision as authorization to run a task or bypass approvals.

## Next milestone

v1.1: Reduce installation size, introduce a friendly verification/refresh flow for signed-in Copilot pricing/account identity (with explicit consent), and capture genuinely matched executions for forecasting quality checks, while retaining consent, provider isolation and deterministic budget gates.
