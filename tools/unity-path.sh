#!/usr/bin/env bash
# Prints the Unity editor binary matching ProjectSettings/ProjectVersion.txt (macOS).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VER="$(grep -m1 'm_EditorVersion:' "$ROOT/ProjectSettings/ProjectVersion.txt" | awk '{print $2}')"
BIN="/Applications/Unity/Hub/Editor/$VER/Unity.app/Contents/MacOS/Unity"
[[ -x "$BIN" ]] || { echo "Unity $VER not found at $BIN" >&2; exit 1; }
echo "$BIN"
