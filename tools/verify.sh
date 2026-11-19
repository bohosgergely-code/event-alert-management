#!/usr/bin/env bash
# One command to check the whole repository, the way a reviewer would.
#   bash tools/verify.sh
# It builds and tests the backend and the frontend, builds the frontend for production, and
# checks that the committed API description and generated TypeScript types match the running API.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 CI=1 NG_CLI_ANALYTICS=false
step() { printf '\n=== %s\n' "$1"; }

step "backend: build and test"
dotnet test "$ROOT" 2>&1 | grep -E "error|^(Passed|Failed)!" | sort -u

step "frontend: install, test, production build"
cd "$ROOT/src/EventAlertManagement.Web"
[ -d node_modules ] || npm ci --silent
npx ng test --watch=false 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -E "Tests |FAIL"
npx ng build 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -E "Initial total|error|complete"

step "contract: committed API description and generated types are current"
cp "$ROOT/docs/openapi.json" /tmp/openapi.committed.json
cp src/app/core/api-schema.ts /tmp/api-schema.committed.ts
bash "$ROOT/tools/export-openapi.sh" >/dev/null
npm run --silent generate:api >/dev/null
if cmp -s /tmp/openapi.committed.json "$ROOT/docs/openapi.json" && cmp -s /tmp/api-schema.committed.ts src/app/core/api-schema.ts; then
  echo "up to date"
else
  echo "STALE: the API description or the generated types differ from what is committed (re-run tools/export-openapi.sh and npm run generate:api, then commit)"; exit 1
fi

printf '\nALL CHECKS PASSED\n'
