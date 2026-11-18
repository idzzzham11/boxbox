#!/usr/bin/env bash
# Build locally, ship to a Linux server over SSH, restart. The database (/var/lib/boxbox) is never touched.
# Usage:  DEPLOY_HOST=root@<host> [ARCH=linux-x64|linux-arm64] ./deploy/deploy.sh
set -euo pipefail

: "${DEPLOY_HOST:?Set DEPLOY_HOST, e.g. root@my-server.example.com}"
ARCH="${ARCH:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

echo "==> Tests"
dotnet test "$ROOT" --nologo -v q

echo "==> Publish ($ARCH, self-contained)"
dotnet publish "$ROOT/src/BoxBox.Api" -c Release -r "$ARCH" --self-contained -o "$OUT/app" --nologo -v q

# The Blazor WebAssembly front end runs in the browser, so it is not tied to the server's CPU architecture.
# Its output is served by the API under /next/.
echo "==> Publish web app (Blazor WebAssembly)"
dotnet publish "$ROOT/src/BoxBox.Web" -c Release -o "$OUT/web" --nologo -v q
mkdir -p "$OUT/app/wwwroot/next"
cp -r "$OUT/web/wwwroot/." "$OUT/app/wwwroot/next/"

echo "==> Upload release $STAMP"
ssh "$DEPLOY_HOST" "mkdir -p /opt/boxbox/releases /var/lib/boxbox"
rsync -az --delete "$OUT/app/" "$DEPLOY_HOST:/opt/boxbox/releases/$STAMP/"
scp -q "$ROOT/deploy/boxbox.service" "$DEPLOY_HOST:/etc/systemd/system/boxbox.service"

echo "==> Switch, restart, health-check"
ssh "$DEPLOY_HOST" bash -s -- "$STAMP" <<'REMOTE'
set -euo pipefail
stamp="$1"
chmod +x "/opt/boxbox/releases/$stamp/BoxBox.Api"

# Previous release: only trust a symlink that really points into releases/.
# `readlink -f` prints the path itself when the target is missing, which used to
# leave /opt/boxbox/current pointing at itself (203/EXEC on restart).
prev=""
if [ -L /opt/boxbox/current ]; then
  cand="$(readlink /opt/boxbox/current || true)"
  case "$cand" in
    /opt/boxbox/releases/*)
      if [ -d "$cand" ]; then prev="$cand"; fi
      ;;
  esac
fi

ln -sfn "/opt/boxbox/releases/$stamp" /opt/boxbox/current
systemctl daemon-reload
systemctl enable boxbox >/dev/null
systemctl restart boxbox
for i in $(seq 1 20); do
  if curl -fs http://127.0.0.1:5080/api/summary >/dev/null && curl -fs http://127.0.0.1:5080/next/ >/dev/null; then
    echo "healthy: $stamp"
    ls -dt /opt/boxbox/releases/* | tail -n +4 | xargs -r rm -rf   # keep last 3
    exit 0
  fi
  sleep 1
done
echo "UNHEALTHY - rolling back" >&2
journalctl -u boxbox -n 20 --no-pager >&2 || true
if [ -n "$prev" ]; then ln -sfn "$prev" /opt/boxbox/current; systemctl restart boxbox; fi
exit 1
REMOTE
echo "==> Done"
