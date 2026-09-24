#!/usr/bin/env bash
# Builds the Android APK to Builds/Fieldmate.apk via Fieldmate.Editor.BuildScript.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="$("$ROOT/tools/unity-path.sh")"
mkdir -p "$ROOT/Builds"
"$UNITY" -batchmode -nographics -quit -projectPath "$ROOT" -buildTarget Android \
  -executeMethod Fieldmate.Editor.BuildScript.BuildAndroid -logFile "$ROOT/Builds/build.log"
ls -la "$ROOT/Builds/"*.apk
