# Fieldmate

AI-assisted mixed-reality maintenance companion for Meta Quest 3 — Unity 6, OpenXR, XR Interaction Toolkit.

Work in progress. Progress is tracked in the issues and milestones; a write-up follows with the 1.0 release.

## Build
Requires Unity 6000.4.9f1 (`ProjectSettings/ProjectVersion.txt`; see `docs/adr/002-unity-6000-4-supported-stream.md`) with Android Build Support, and a Quest 3 on Horizon OS v74+.

```
tools/test.sh      # EditMode + PlayMode tests
tools/build.sh     # Android APK -> Builds/
tools/install.sh   # adb install -r + launch
```

After changing project settings by hand, `Fieldmate > Configure Project for Quest` re-applies the Quest defaults.

Copy `Assets/_Project/Secrets/keys.json.template` to `keys.json` and add your API keys to enable the assistant.

## Licence
MIT for the code in this repository. Third-party assets are not included; see `docs/ASSETS.md`.
