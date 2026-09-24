#!/usr/bin/env bash
# Builds the Android APK to Builds/Fieldmate.apk via Fieldmate.Editor.BuildScript.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="$("$ROOT/tools/unity-path.sh")"
LOG="$ROOT/Builds/build.log"
mkdir -p "$ROOT/Builds"
echo "==> Building Android APK (log: Builds/build.log)"
if ! "$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget Android \
  -executeMethod Fieldmate.Editor.BuildScript.BuildAndroid -logFile "$LOG"; then
  echo "==> Build FAILED; last errors:"
  grep -E "error|Exception|Build (Failed|failed)" "$LOG" | grep -v "Licensing::" | tail -15 | sed 's/^/   /' || true
  exit 1
fi
grep -E "^Build (Succeeded|Failed)" "$LOG" | tail -1 || true
ls -la "$ROOT/Builds/Fieldmate.apk"
