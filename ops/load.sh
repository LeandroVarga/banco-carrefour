#!/usr/bin/env bash
set -euo pipefail

RPS=50
DUR="15s"
MAX_LOSS=0.05
DATE="$(date +%F)"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rps) RPS="$2"; shift 2 ;;
    --duration) DUR="$2"; shift 2 ;;
    --max-loss) MAX_LOSS="$2"; shift 2 ;;
    --date) DATE="$2"; shift 2 ;;
    *) DATE="$1"; shift ;;
  esac
done

URL="http://localhost:8080/balances/daily?date=${DATE}"
SECS=${DUR%s}

echo "Load: ${RPS} rps for ${SECS}s on ${URL} (max-loss ${MAX_LOSS})"

ok=0
err=0

req_batch() {
  local out="$1"
  local code
  code=$(curl -s -m 3 -o /dev/null -w "%{http_code}" "$URL" || echo "000")
  echo "$code" >> "$out"
}

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT

for ((t=0;t<SECS;t++)); do
  outfile="$tmpdir/codes_$t.txt"
  : > "$outfile"
  # spawn RPS concurrent GETs
  seq 1 "$RPS" | xargs -I{} -P "$RPS" bash -c 'req_batch "$0"' "$outfile"
  # tally this second
  while read -r c; do
    if [[ "$c" =~ ^2[0-9][0-9]$ ]]; then ok=$((ok+1)); else err=$((err+1)); fi
  done < "$outfile"
done

total=$((ok+err))
loss=0
if (( total > 0 )); then
  loss=$(awk -v e=$err -v t=$total 'BEGIN{ printf "%.6f", (e/t) }')
fi
echo "Summary: total=$total 2xx=$ok errors=$err loss=$loss"

awk -v l=$loss -v m=$MAX_LOSS 'BEGIN{ if (l>m) exit 1 }' || { echo "FAIL: loss ratio > max-loss"; exit 1; }
echo "OK"
