"""Exercise the packaged AgentForge CLI over the MCP stdio wire protocol.

This runs only in CI against the freshly installed .NET tool. It uses the
Python standard library, never sends prompt content outside the local process,
and makes no modifications to a repository.
"""

import json
import os
import queue
import subprocess
import sys
import tempfile
import threading
from pathlib import Path


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("Usage: smoke_mcp.py <agentforge-executable>")

    executable = Path(sys.argv[1]).resolve(strict=True)
    process = subprocess.Popen(
        [str(executable), "mcp"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )
    messages: queue.Queue[str | None] = queue.Queue()

    def read_stdout() -> None:
        assert process.stdout is not None
        for line in process.stdout:
            messages.put(line)
        messages.put(None)

    threading.Thread(target=read_stdout, daemon=True).start()

    def send(payload: dict) -> None:
        assert process.stdin is not None
        process.stdin.write(json.dumps(payload, separators=(",", ":")) + "\n")
        process.stdin.flush()

    def request(request_id: int, method: str, params: dict | None = None) -> dict:
        send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params or {}})
        while True:
            line = messages.get(timeout=30)
            if line is None:
                raise RuntimeError(f"MCP server exited before responding to {method}")
            response = json.loads(line)
            if response.get("id") == request_id:
                if "error" in response:
                    raise RuntimeError(f"MCP {method} failed: {response['error']}")
                return response["result"]

    try:
        initialized = request(
            1,
            "initialize",
            {
                "protocolVersion": "2025-11-25",
                "capabilities": {},
                "clientInfo": {"name": "agentforge-ci-smoke", "version": "1.0"},
            },
        )
        assert "protocolVersion" in initialized, initialized
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})

        available = request(2, "tools/list")
        tools = [tool["name"] for tool in available.get("tools", [])]
        assert "preflight_task" in tools, f"AgentForge MCP tool missing: {tools}"

        result = request(
            3,
            "tools/call",
            {"name": "preflight_task", "arguments": {"prompt": "Refactor the lighting engine"}},
        )
        assert not result.get("isError"), result
        content = "\n".join(item.get("text", "") for item in result.get("content", []))
        assert "Tokens P10 / P50 / P90" in content, content
        assert "no task executed" in content, content
        print("MCP smoke passed: initialize, tools/list, preflight_task.")
    finally:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)

    # The installed CLI must route the new doctor command, report missing
    # configuration conservatively, and avoid creating a settings file.
    with tempfile.TemporaryDirectory(prefix="agentforge-doctor-") as workspace:
        settings = Path(workspace) / "preferences.json"
        environment = os.environ.copy()
        environment["AGENTFORGE_CONFIG_PATH"] = str(settings)
        doctor = subprocess.run(
            [str(executable), "doctor"],
            cwd=workspace,
            env=environment,
            capture_output=True,
            text=True,
            timeout=15,
            check=False,
        )
        assert doctor.returncode == 2, doctor.stdout + doctor.stderr
        assert "Readiness: ACTION REQUIRED" in doctor.stdout, doctor.stdout
        assert "agentforge vscode init" in doctor.stdout, doctor.stdout
        assert not settings.exists(), "Doctor must never save configuration"
        print("CLI smoke passed: read-only doctor reports missing setup.")

        # Even when a model has been configured, a refusal must never start
        # the Copilot SDK or alter the user's saved preferences.
        saved_preferences = {
            "provider": "GitHubCopilot",
            "modelId": "chosen-model",
            "accountId": "personal",
            "monthlyAllowance": 30000,
            "alreadyUsed": 9000,
            "balanceObservedAt": "2026-09-01T00:00:00+00:00",
        }
        original_preferences = json.dumps(saved_preferences)
        settings.write_text(original_preferences, encoding="utf-8")
        declined = subprocess.run(
            [str(executable), "copilot", "status"],
            cwd=workspace,
            env=environment,
            input="no\n",
            capture_output=True,
            text=True,
            timeout=15,
            check=False,
        )
        assert declined.returncode == 2, declined.stdout + declined.stderr
        assert "No SDK call made" in declined.stdout, declined.stdout
        assert settings.read_text(encoding="utf-8") == original_preferences
        print("CLI smoke passed: Copilot status refusal makes no SDK call.")


if __name__ == "__main__":
    main()
