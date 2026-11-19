#!/usr/bin/env bash
# Starts the API briefly, saves its OpenAPI document to docs/openapi.json and stops it again.
# Run from the repository root:  bash tools/export-openapi.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TMP="$(mktemp -d)"
PORT=5099
cd "$ROOT/src/EventAlertManagement.Api"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
  ConnectionStrings__Default="Data Source=$TMP/export.db" DataProtection__KeysPath="$TMP/keys" Workers__Enabled=false \
  dotnet run --no-launch-profile > "$TMP/server.log" 2>&1 &
PID=$!
trap 'kill $PID 2>/dev/null || true; rm -rf "$TMP"' EXIT
for _ in $(seq 1 60); do
  curl -sf "http://127.0.0.1:$PORT/swagger/v1/swagger.json" -o "$ROOT/docs/openapi.json" && break
  sleep 1
done
python3 -c "import json,sys; d=json.load(open('$ROOT/docs/openapi.json')); print('exported', len(d['paths']), 'paths,', len(d['components']['schemas']), 'schemas')"
