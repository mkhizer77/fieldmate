#!/usr/bin/env bash
# Installs Builds/Fieldmate.apk (or the APK given as $1) on the connected Quest and launches it.
# Uses a push install: the default streamed install drops the Quest's USB connection mid-transfer.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APK="${1:-$ROOT/Builds/Fieldmate.apk}"
PKG="${PKG:-com.khizerkhalid.fieldmate}"
[[ -f "$APK" ]] || { echo "APK not found: $APK (run tools/build.sh)"; exit 1; }
# The headset can vanish from adb for a few seconds (sleep, USB renegotiation); wait up to 15 s.
for _ in $(seq 1 15); do adb devices | grep -q "device$" && break; sleep 1; done
adb devices | grep -q "device$" || { echo "No Quest connected (enable developer mode + USB debugging)"; exit 1; }
adb install -r --no-streaming "$APK"
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null
echo "Launched $PKG"
