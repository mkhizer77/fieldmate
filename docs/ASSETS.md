# Assets (not tracked in git)

Paid or licence-restricted assets live in `Assets/ThirdParty/` (gitignored). The project must
build and run with the placeholders in `Assets/_Project/Placeholders/` when they are absent.

| Purpose | Asset | Source / licence | Import to |
|---|---|---|---|
| Pump skid (motor, pump, valves, cabinet) | TBD | Unity Asset Store | Assets/ThirdParty/Machine/ |
| Hand tools (wrench, multimeter, gloves) | TBD | Unity Asset Store | Assets/ThirdParty/Tools/ |
| Industrial UI icons | TBD | | Assets/ThirdParty/UI/ |
| Ambient sound | TBD | | Assets/ThirdParty/Audio/ |

Rules: no real brand names on machines; keep the total under ~120 €; note the licence here.

## Tracked free assets

| Purpose | Asset | Licence | Where |
|---|---|---|---|
| UI typeface | Inter 4.1 (Regular, SemiBold) | SIL OFL 1.1 (`Assets/_Project/Fonts/Inter-LICENSE.txt`) | `Assets/_Project/Fonts/`, SDF atlases in `Assets/_Project/Resources/Fonts/` (rebuild: Fieldmate → Build UI Fonts) |
| TextMeshPro essentials | Unity's TMP resources (shaders, LiberationSans SDF fallback) | Unity Companion License | `Assets/TextMesh Pro/` |
| Hand presence fallback models | XR Hands package sample (LeftHand/RightHand.fbx) | Unity Companion License | `Assets/_Project/Presence/Models/` (used only where the runtime has no fitted hand mesh) |
| Controller presence model | XRI Starter Assets sample (UniversalController.fbx) | Unity Companion License | `Assets/_Project/Presence/Models/` (rebuild prefabs: Fieldmate → Build Presence Prefabs) |
