#!/usr/bin/env bash
# Runs EditMode and PlayMode tests in batch mode. Usage: tools/test.sh [editmode|playmode|all]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="$("$ROOT/tools/unity-path.sh")"
MODE="${1:-all}"
OUT="$ROOT/TestResults"; mkdir -p "$OUT"
STATUS=0
run() {
  local platform="$1"
  echo "==> $platform tests"
  # PlayMode needs graphics; EditMode does not.
  local extra=(); [[ "$platform" == "EditMode" ]] && extra=(-nographics)
  "$UNITY" -batchmode "${extra[@]}" -projectPath "$ROOT" -runTests -testPlatform "$platform" \
    -testResults "$OUT/$platform.xml" -logFile "$OUT/$platform.log" || STATUS=$?
  python3 - "$OUT/$platform.xml" <<'PY'
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
exit $STATUS
