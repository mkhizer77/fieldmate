#!/usr/bin/env bash
# Copies Assets/_Project/Secrets/keys.json (proxy URL + optional dev token) to the app's storage on the Quest,
# where AssistantProviders reads it at runtime. The file never goes into the APK or the repo (ADR-003).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KEYS="$ROOT/Assets/_Project/Secrets/keys.json"
PKG="${PKG:-com.khizerkhalid.fieldmate}"
[[ -f "$KEYS" ]] || { echo "Missing $KEYS (copy keys.json.template and fill it in)"; exit 1; }
python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$KEYS" || { echo "keys.json is not valid JSON"; exit 1; }
adb devices | grep -q "device$" || { echo "No Quest connected"; exit 1; }
adb push "$KEYS" "/sdcard/Android/data/$PKG/files/keys.json" >/dev/null
echo "keys.json pushed to $PKG (restart the app to pick it up)"
