# TokenBudget development conventions

This is a .NET **10** solution with a small reusable core and separate delivery hosts.
Keep domain and estimation code independent of HTTP, CLI, MCP, SQLite and optional LLM review.

## Dependency direction

1. `TokenBudget.Domain` owns stable records, enums, provider capabilities and interfaces.
   Do not reference Microsoft Agent Framework, ASP.NET Core or Microsoft.Data.Sqlite here.
2. `TokenBudget.Application` owns deterministic parsing, counting, simulation, budget policy
   and repository path validation. Depend on domain interfaces, not host-specific types.
3. `TokenBudget.Provider.Codex` owns Codex event parsing. `TokenBudget.Telemetry` owns
   local ingestion and the optional SQLite history. Do not persist transcripts.
4. `TokenBudget.Agent` owns opt-in review. Its narrative cannot change the deterministic
   estimate, the decision, or an approval requirement.
5. `TokenBudget.Cli`, `TokenBudget.Api`, `TokenBudget.Mcp` and `TokenBudget.Agent.Cli`
   are adapters. Prefer a small handler or extension method over adding logic to `Program.cs`.

## Placement guide

- New plan or forecast field: add to the relevant domain contract; consider JSON compatibility.
- New provider capability: update `ProviderCapabilities` and the appropriate provider adapter.
- New CLI command: add one file in `src/TokenBudget.Cli/Commands/`, route it in `CliApplication`,
  parse only supported options through `CliOptions`, and add MSTest coverage.
- New HTTP route: add handler in `src/TokenBudget.Api/Endpoints/`. Preserve
  `TokenBudgetSecurityMiddleware` for request-size, authentication and opt-in gates.
- New telemetry source: add a source reader and maintain per-run deduplication; explicitly
  distinguish complete, partial and repeated cumulative counters.
- New integration: state precisely what can and cannot be observed. Never claim exact future
  tool traffic or reliable P90 coverage without completed matched-run evidence.

## Formatting and tests

- C# uses four-space indentation, file-scoped namespaces, UTF-8 BOM and CRLF.
- Use braces around control-flow branches; keep methods focused and lines under 180 characters.
- Public APIs receive XML summaries; package versions live in `Directory.Packages.props`.
- Tests use MSTest and deterministic inputs, with cancellation-aware tests when applicable.
- Never access a real provider in a unit test. Validate live integrations separately, with
  explicit credentials, consent, safe plans and provider-cost accounting.

Build and verify from the solution root:

    dotnet restore .\TokenBudget.slnx
    dotnet build .\TokenBudget.slnx -c Release --no-restore -m:1
    dotnet test .\TokenBudget.slnx -c Release --no-build -m:1

## Safety invariants

Estimation is read-only. A local repo scan never executes plan instructions or follows symlinks.
The MCP server remains offline and requires an allowlisted repository root for scanning. The
HTTP API stays local-only without a configured key. Remote review remains disabled unless
the server explicitly opts in and the caller explicitly authorizes plan-text upload.
