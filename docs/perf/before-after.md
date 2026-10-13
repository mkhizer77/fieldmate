# Performance pass (#29): before and after

Goal: the app's frame work fits in **10–11 ms** on Quest 3 at 72 Hz (13.9 ms display interval, passthrough on), with
headroom for the compositor.

## What the first numbers really said

The 2026-09-30 captures (`budget.md`) read "cpu 13.9 ms, frame 13.9 ms". That CPU figure was
`FrameTiming.cpuFrameTime`, which includes waiting for the next display refresh. It sits at the display interval
whenever the app keeps up, so it says the app hit 72 fps, not how much of the 13.9 ms was work. The GPU read 0 because
the platform doesn't report it there.

`FrameTimeProbe` now logs the work itself:

```
[Perf] occlusion=Hard main 6.1 ms render 4.3 ms gpu 9.1 ms cpu 13.9 ms frame 13.9 ms (360 frames)
```

- `main`: main-thread work without its wait for present.
- `render`: render-thread work.
- `gpu`: GPU time, where the runtime reports it.

`main`, `render` and `gpu` are the numbers that have to fit the budget. `QuestPerformance` logs once at start what the
headset accepted:

```
[Perf] foveation 1.00 (on), levels cpu=SustainedHigh gpu=SustainedHigh (accepted)
```

## Measured in the editor (same work per frame, Mac timings)

`PerfBenchmarkTests` renders the bench from the user's working position (machine placed, procedure running) into a
Quest-eye-sized target, 300 frames. It writes `TestResults/perf-bench.md`.

| Per frame | Before | After | Notes |
|---|---|---|---|
| SetPass calls, normal view (mate + panels + machine) | **60** | **16** | Hologram depth pass moved to one renderer feature (below) |
| SetPass calls, worst case (every part label shown) | **102** | **86** | Each label card is ~3 UI batches; labels only show on request |
| SetPass calls by source, before | machine 6 · **mate 28** · UI 14 · other 2 | machine 6 · mate ~4 · UI 14 · other 2 | The machine was already SRP-batched |
| Triangles in view | 26 k | 26 k | Budget 400 k; LODs would buy nothing on primitive geometry |
| Managed allocations from the app | 0 B | 0 B | Measured against the editor's own floor (~700 B/frame with every Fieldmate behaviour off); guarded by the test |
| Our scripts (Update + LateUpdate) | 0.08 ms | 0.08 ms | Mac timing; small on any device |

The mate looks the same after the change (compared side by side, rendered in the editor). If anything, the shoulders
and the top of the head stack less, because every hologram part now writes depth before any of them is lit.

## What changed and why it matters on the headset

| Change | Where | Expected effect on Quest 3 |
|---|---|---|
| **Fixed foveated rendering on** (level 1.0, high). The Android feature was off, only Standalone had it | `FoveatedRenderingFeature` (OpenXR, Android), `QuestPerformance` | The largest GPU saving available: fewer shaded pixels towards the lens edges, where the lenses blur anyway. Typically 10–25 % of GPU fragment time. Peripheral text softens slightly |
| **CPU/GPU level hints: SustainedHigh** | `XrPerformanceSettingsFeature`, `QuestPerformance` | The runtime keeps clocks up instead of scaling down mid-procedure (dynamic levels start low) |
| **Hologram depth pre-pass as a renderer feature** | `Quest_Renderer` "Hologram Depth" (RenderObjects, before transparents), `Hologram.shader` | Mate SetPass 28 → ~4. Fewer state changes on the render thread every frame the mate is in view |
| **Hidden canvases disabled**, not just alpha 0 (Stats overlay, gaze ring) | `PerfOverlay`, `GazeRing` | A canvas at alpha 0 still rebuilds and submits its geometry every frame |
| **No stack traces for `Debug.Log` / warnings** in the player (errors and exceptions keep them) | Player settings | A stack trace per log line costs ~1 ms on Quest. The app logs `[Perf]`, `[Interaction]` and `[Assistant]` lines regularly, and each was a frame spike |
| **Optimized Frame Pacing (Swappy) off** | Player settings | The OpenXR compositor paces frames; Swappy on top can add a frame of wait |
| **GPU skinning (batched compute)** | Player settings | The two tracked-hand meshes (1360 vertices each) no longer skin on the main thread |
| **Main-light shadows unsupported** | `Quest_URP` | Nothing cast shadows; this removes the shadow variants and makes sure no light ever turns a shadow pass on |

Unchanged on purpose:
- **MSAA 4×**: resolved on tile on Quest's GPU, close to free, and essential for edges in passthrough.
- **Render scale 1.0**: text on the panels needs it, and foveation takes the savings at the edges instead.
- **Depth/opaque textures**: already off.
- **Multiview**: already on.
- **SRP Batcher**: already working for the machine.
- **Textures**: the only project texture is the 1024² gauge dial, ASTC with mips.

All of these are pinned by `QuestPerfSettingsTests` and re-applied by `Fieldmate > Configure Project for Quest`.

## To measure on the headset (not done yet: no device in this pass)

1. `tools/build.sh && tools/install.sh`. Place the machine and stand at working distance with the mate in view.
2. `adb logcat -s Unity | grep "\[Perf\]"`: check the foveation/levels line, then take 30 s per condition:
   - normal procedure
   - labels on ("what is what?")
   - hands vs controllers
   - Hard vs Soft occlusion
3. Fill in:

| Condition | main ms | render ms | gpu ms | Date, build |
|---|---|---|---|---|
| Normal, Hard occlusion | | | | |
| Labels on | | | | |
| Hands tracked | | | | |
| Soft occlusion | | | | |

4. If `max(main, render, gpu)` is still above 11 ms, the next steps in order, each one setting:
   - multiview render regions (`MetaQuestFeature`, FinalPass)
   - symmetric projection (check depth occlusion still lines up)
   - MSAA 2×
   - foveation dynamic/high-top
   - render scale 0.9
   - fewer label cards (one shared canvas)

   Turn on one at a time and re-measure.
