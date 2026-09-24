#!/usr/bin/env bash
# Installs Builds/Fieldmate.apk on the connected Quest and launches it.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APK="${1:-$ROOT/Builds/Fieldmate.apk}"
PKG="${PKG:-com.khizerkhalid.fieldmate}"
adb devices | grep -q "device$" || { echo "No Quest connected (enable developer mode + USB debugging)"; exit 1; }
adb install -r "$APK"
adb shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null
echo "Launched $PKG"
