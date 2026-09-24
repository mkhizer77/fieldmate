#!/usr/bin/env bash
# Runs EditMode and PlayMode tests in batch mode. Usage: tools/test.sh [editmode|playmode|all]
# Exit code is non-zero if any suite fails or produces no results (e.g. compile errors).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="$("$ROOT/tools/unity-path.sh")"
MODE="${1:-all}"
case "$MODE" in editmode|playmode|all) ;; *) echo "usage: $0 [editmode|playmode|all]" >&2; exit 2 ;; esac
OUT="$ROOT/TestResults"; mkdir -p "$OUT"
STATUS=0
run() {
  local platform="$1"
  local xml="$OUT/$platform.xml" log="$OUT/$platform.log"
  echo "==> $platform tests"
  rm -f "$xml"
  # PlayMode needs graphics; EditMode does not. (No arrays: macOS bash 3.2 + set -u rejects empty ones.)
  local nographics=""; [[ "$platform" == "EditMode" ]] && nographics="-nographics"
  "$UNITY" -batchmode $nographics -projectPath "$ROOT" -runTests -testPlatform "$platform" \
    -testResults "$xml" -logFile "$log" || STATUS=$?
  if [[ ! -f "$xml" ]]; then
    echo "   no results written; last errors from $log:"
    grep -E "error CS|Exception|Aborting" "$log" | tail -10 | sed 's/^/   /' || true
    STATUS=1
    return
  fi
  python3 - "$xml" <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
print(f"   total={r.get('total')} passed={r.get('passed')} failed={r.get('failed')} skipped={r.get('skipped')}")
for tc in r.iter('test-case'):
    if tc.get('result') == 'Failed':
        print(f"   FAILED: {tc.get('fullname')}")
PY
}
[[ "$MODE" == "all" || "$MODE" == "editmode" ]] && run EditMode
[[ "$MODE" == "all" || "$MODE" == "playmode" ]] && run PlayMode
[[ $STATUS -eq 0 ]] && echo "==> all green" || echo "==> FAILED (exit $STATUS)"
exit $STATUS
