#!/usr/bin/env bash
# Builds the macOS remote observer to Builds/FieldmateObserver.app via Fieldmate.Editor.BuildScript (#26).
# Switching to the macOS target re-imports assets once; tools/build.sh switches back to Android.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="$("$ROOT/tools/unity-path.sh")"
LOG="$ROOT/Builds/build-observer.log"
mkdir -p "$ROOT/Builds"
rm -f "$LOG"
echo "==> Building the macOS observer (log: Builds/build-observer.log)"
if ! "$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget OSXUniversal \
  -executeMethod Fieldmate.Editor.BuildScript.BuildObserverMac -logFile "$LOG"; then
  echo "==> Build FAILED; last errors:"
  grep -E "error|Exception|Build (Failed|failed)" "$LOG" | grep -v "Licensing::" | tail -15 | sed 's/^/   /' || true
  exit 1
fi
grep -E "^Build (Succeeded|Failed)" "$LOG" | tail -1 || true
echo "==> open Builds/FieldmateObserver.app"
