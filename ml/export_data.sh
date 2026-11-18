#!/usr/bin/env bash
# Copy the fill-ups from the server's database into ml/data/entries.csv (git-ignored: it is private data).
# Usage: BOXBOX_HOST=root@my-server ./ml/export_data.sh
set -euo pipefail
: "${BOXBOX_HOST:?Set BOXBOX_HOST, e.g. root@my-server}"
DB="${BOXBOX_DB:-/var/lib/boxbox/boxbox.db}"
OUT="$(cd "$(dirname "$0")" && pwd)/data/entries.csv"
mkdir -p "$(dirname "$OUT")"
ssh "$BOXBOX_HOST" "sqlite3 -header -csv '$DB' 'select Date as date, AmountPaid as amount, Litres as litres, TripKm as trip_km from Entries order by Date, CreatedUtc'" > "$OUT"
echo "wrote $(($(wc -l < "$OUT") - 1)) fill-ups to $OUT"
