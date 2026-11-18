#!/usr/bin/env bash
# Build the API and the Blazor front end into a temporary folder and run them together on a local port.
# Usage: ./scripts/run-local.sh [port]      (default port 5080, data in ./local-data/boxbox.db)
set -euo pipefail

PORT="${1:-5080}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

dotnet publish "$ROOT/src/BoxBox.Api" -c Release -o "$OUT/app" --nologo -v q
dotnet publish "$ROOT/src/BoxBox.Web" -c Release -o "$OUT/web" --nologo -v q
cp -r "$OUT/web/wwwroot/." "$OUT/app/wwwroot/"

mkdir -p "$ROOT/local-data"
echo "BoxBox on http://localhost:$PORT  (Ctrl+C to stop; database: $ROOT/local-data/boxbox.db)"
ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
ConnectionStrings__BoxBox="Data Source=$ROOT/local-data/boxbox.db" \
dotnet "$OUT/app/BoxBox.Api.dll"
