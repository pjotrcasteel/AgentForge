#!/usr/bin/env bash
set -euo pipefail

# Starts an isolated, synthetic-key API instance and checks local security boundaries.
tb_root=$(cd "$(dirname "$0")/.." && pwd)
tb_dotnet="${TOKENBUDGET_DOTNET:-dotnet}"
tb_url="http://127.0.0.1:5172"
tb_key="local-smoke-key"

TOKENBUDGET_API_KEY="$tb_key" TOKENBUDGET_REPO_ROOT="$tb_root" \
    "$tb_dotnet" "$tb_root/src/TokenBudget.Api/bin/Release/net10.0/TokenBudget.Api.dll" \
    --urls "$tb_url" >/dev/null 2>&1 &
tb_server_pid=$!

cleanup() {
    kill "$tb_server_pid" 2>/dev/null || true
    wait "$tb_server_pid" 2>/dev/null || true
}
trap cleanup EXIT

tb_ready=false
for attempt in 1 2 3 4 5 6 7 8; do
    if curl --fail --silent "$tb_url/health" >/dev/null 2>&1; then
        tb_ready=true
        break
    fi
    sleep 1
done

if [[ "$tb_ready" != true ]]; then
    echo "API failed to start on $tb_url" >&2
    exit 1
fi

assert_http() {
    local label="$1"
    local expected="$2"
    shift 2
    local actual
    actual=$(curl --silent --show-error -o /dev/null -w '%{http_code}' "$@")
    if [[ "$actual" != "$expected" ]]; then
        echo "$label expected $expected, received $actual" >&2
        exit 1
    fi
    echo "$label: HTTP $actual"
}

tb_request='{"plan":"## M0 - Verify\nReview tests.","modelId":"offline-test-model","target":"CodexCli","budget":{"maxCumulativeTokens":500000},"simulationCount":1000}'
tb_outside='{"plan":"## M0 - Verify\nReview tests.","modelId":"offline-test-model","target":"CodexCli","budget":{"maxCumulativeTokens":500000},"repositoryPath":"/tmp","simulationCount":1000}'

assert_http "Health" 200 "$tb_url/health"
assert_http "Unauthorized capabilities" 401 "$tb_url/v1/capabilities"
assert_http "Authorized capabilities" 200 -H "X-TokenBudget-Key: $tb_key" "$tb_url/v1/capabilities"
assert_http "Authorized estimate" 200 -H "X-TokenBudget-Key: $tb_key" -H "Content-Type: application/json" \
    --data "$tb_request" "$tb_url/v1/estimates"
assert_http "Outside repository denied" 403 -H "X-TokenBudget-Key: $tb_key" -H "Content-Type: application/json" \
    --data "$tb_outside" "$tb_url/v1/estimates"
assert_http "Agent review opt-in required" 403 -H "X-TokenBudget-Key: $tb_key" -H "Content-Type: application/json" \
    --data '{}' "$tb_url/v1/agent/preflight"
