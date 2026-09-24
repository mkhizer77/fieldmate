# ADR-002: Unity 6000.4 (Supported stream) instead of Unity 6 LTS

Date: 2026-09-24 · Status: accepted

(ADR-001 is reserved for the OpenXR Meta platform spike, #3.)

## Context
The design fixes the engine as "Unity 6 LTS (6000.x)". On the development Mac the only Unity 6 editors installed
are 6000.4.6f1 and 6000.4.9f1, both on the Supported (Update) stream; the current LTS builds are 6000.3.x and
6000.0.x. GameCI publishes a Linux image for 6000.4.9f1 (`unityci/editor:ubuntu-6000.4.9f1-linux-il2cpp-3`), and the
first CI run activated and ran tests with it.

## Decision
Use Unity **6000.4.9f1**. `ProjectSettings/ProjectVersion.txt` is the source of truth; `.github/workflows/ci.yml`
pins the same version. All other §5.1 decisions (URP, Vulkan, OpenXR + XRI 3.x + XR Hands, AR Foundation via
Unity OpenXR: Meta) are unchanged.

Package versions: latest stable registry release compatible with 6000.4, or the editor's bundled core version for
URP 17.4.0, Test Framework 1.6.0 and uGUI 2.0.0. `com.unity.textmeshpro` is deprecated in Unity 6 (TMP ships
inside uGUI 2.0), so it is not added.

## Consequences
- Newer editor and OpenXR/Meta fixes than the LTS line; Supported releases are fully supported by Unity until the
  next update ships.
- Upgrades land faster and may need a patch bump during the 4 weeks; bump editor, `ProjectVersion.txt` and the CI
  `UNITY_VERSION` in one PR.
- Revisit if the week-1 spike (#3) hits an editor bug that is fixed only on an LTS line.
