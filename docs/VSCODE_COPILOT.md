# AgentForge in VS Code Copilot Chat (v1.1 preview)

AgentForge integrates with GitHub Copilot Chat using a **custom agent and a local .NET MCP server**. This is the current supported VS Code integration mechanism; the older Chat Participant extension API is deprecated for the Copilot harness.

In the normal workflow, you select **AgentForge** from the Copilot agent picker and type **one task**, such as:

> Refactor the lighting engine

Copilot calls AgentForge's `PreflightTask(prompt)` MCP tool to obtain a deterministic token forecast, budget decision, prompt-scope advice and a suggested narrower prompt. **The task is not executed.** Switch back to your implementation agent only after you review the findings.

## One-time Windows setup

Requirements: Visual Studio Code with GitHub Copilot Chat, .NET 10 installed, and the AgentForge .NET tool installed.

1. Download the `agentforge-dotnet10-tool-preview` package artifact from the latest **successful** [AgentForge CI run](https://github.com/pjotrcasteel/AgentForge/actions/workflows/ci.yml) and extract the `.nupkg` file into a local `downloads` folder. This preview is **not yet published to NuGet.org**. Alternatively, clone the source repository and run `dotnet pack .\src\TokenBudget.Cli -c Release --output .\downloads`.
2. In PowerShell, from the folder containing `downloads`, install or update the CLI:

```powershell
dotnet tool install --global --add-source .\downloads AgentForge.TokenBudget.Cli --version 1.1.0-preview.1
# If a prior version is already installed, use 'dotnet tool update' with the same options instead.
agentforge setup
```

3. Open your development project (for example, `Wyrmforge`) as a folder in VS Code. In its terminal run:

```powershell
agentforge vscode init
```

The installer creates `.github/agents/agentforge.agent.md` and `.vscode/mcp.json` in **that workspace only**. It refuses to overwrite conflicting files. If `.vscode/mcp.json` already has other servers, it keeps that file unchanged and uses a separate portable `.mcp.json` when available. No credentials or prompt history are written to either file.

4. In Copilot Chat, choose the **Copilot** harness and select **AgentForge** from the custom-agent picker. Start the `agentforge` MCP server if prompted and review the workspace/MCP trust request. Enter your task in ordinary language; there are no budget switches to type on each request.

5. Expand the tool call and confirm it was **AgentForge → PreflightTask**, not an answer invented by the language model. The tool output should contain token P10/P50/P90, a provider budget decision and prompt-scope guidance. [VS Code documents this verification process](https://code.visualstudio.com/docs/agent-customization/mcp-servers).

## Security and accounting

- The MCP tool is **read-only**. It does not run a development task, modify source, upload the prompt to an AI reviewer, or override a deterministic decision. It inspects only local repository metadata when the VS Code server starts in a Git checkout.
- `agentforge setup` stores non-secret account preferences in your user application-data folder. No keys are written to MCP configuration. Live Copilot **model-pricing lookup** occurs only if you explicitly enabled it during setup; it does not submit task content.
- A Copilot Chat preflight **itself may consume your GitHub AI credits**. Those credits are separate from the *forecasted* future task. AgentForge does not receive a reliable automatic per-chat credit balance from the VS Code model picker.
- Select the actual model in `agentforge setup` to match the one you intend to use in Copilot. Until prices or allowance balances are available, provider cost remains **unknown**, not zero. Your reported 30,000 monthly credits is an account-specific configuration, not a universal default.
- The custom agent exposes only `agentforge/*` MCP tools. It has no edit, shell, or task-execution tools. Switch to a different Copilot agent for implementation after reviewing the forecast and approval gates.
- The MCP prompt argument is always interpreted as **literal task text**, never as a filesystem path. This prevents an AI-supplied filename from causing the tool to read an arbitrary document.

## Troubleshooting

- **`agentforge` not found:** restart VS Code after global tool installation, check `agentforge --help` in the integrated terminal and verify the tool is on your PATH.
- **MCP server not starting:** run `agentforge mcp` in a terminal to check that it starts without writing ordinary text to standard output. Stop it with Ctrl+C, then use **MCP: List Servers → agentforge → Show Output** in VS Code.
- **Agent missing from picker:** check that `.github/agents/agentforge.agent.md` exists in the opened workspace and that the Copilot harness is selected.
- **Existing configuration conflict:** the installer does not overwrite user-owned `.vscode/mcp.json`, `.mcp.json` or custom-agent files. Merge the server entry manually and rerun `agentforge vscode init` only after reviewing it.

## Limitations and next acceptance gate

The C# integration and fixture-based tests can be verified in GitHub Actions, but **live VS Code/Copilot MCP interoperability and authenticated account balances need an opt-in test on your machine**. A passing build does not prove the selected Copilot chat model's credit usage or future-run forecast accuracy.

Official references: [VS Code custom agents](https://code.visualstudio.com/docs/agent-customization/custom-agents), [MCP configuration](https://code.visualstudio.com/docs/agents/reference/mcp-configuration), [Copilot tool selection](https://code.visualstudio.com/docs/agents/reference/custom-agent-file-reference).
