# Performance budget — Quest 3, passthrough on, 72 Hz

Targets from docs/design.md §6. Measured with the in-app **Stats** overlay (button under the step card) and the
`[Perf]` logcat lines; the OVR Metrics Tool is not installed on the test headset (only its background service is),
so GPU time comes from Unity's `FrameTimingManager`, which reports 0 on this platform.

| Item | Budget | Measured 2026-09-30, build from main 1ad4f26 (bench scene, machine placed, hands tracked) |
|---|---|---|
| App frame time | ≤ 11 ms | **13.9 ms** steady (72 Hz frame budget; app is on the edge). CPU 13.9–15.9 ms; GPU not reported |
| Draw calls | ≤ 120 | pending: first build with the Stats overlay (#18) |
| Triangles | ≤ 400 k | pending: same |
| Texture memory | ≤ 300 MB | pending: same ("System Used Memory" and "GC Reserved" from the overlay) |
| GC alloc / frame (interaction + telemetry) | 0 | by construction (guarded in review); not yet profiled on device |

Spikes seen in the same session, for the record: 27 s and 2.1 s single samples while the headset ran a system screen
(permission dialog, Space Setup); 0.8 s while Space Setup was open. Not app frames.

## What's in the frame today (from the code, not yet counted)
- Machine: ~90 primitive renderers on 10 shared `Fieldmate/OccludedLit` materials (SRP batcher, instancing on).
- UI: 12 world canvases (step card, assistant panel, 5 control tags, 4 button labels, gaze ring / perf overlay when
  shown), each ~2 batches (one sliced sprite, one TMP atlas), all on the overlay queue with no depth test.
- Presence: 2 skinned hand meshes (1360 vertices, 2314 triangles each; 3-pass stencil outline) or 2 controller meshes.
- Occlusion: environment depth (Fastest) + 1 or 5 depth samples per machine pixel (Hard / Soft).

## How to capture

1. `tools/build.sh && tools/install.sh`, put the headset on, place the machine, raise both hands.
2. Press **Stats** under the step card. Read fps / ms, draws, batches, SetPass, triangles, memory; or grep logcat:

   ```
   adb logcat -s Unity | grep -E "\[Perf\] (stats|occlusion)"
   [Perf] occlusion=Hard cpu 13.9 ms gpu 0.0 ms frame 13.9 ms (361 frames)
   [Perf] stats frame 13.9 ms draws 84 batches 40 setpass 22 tris 118500 mem 412 MB gc 18 MB
   ```

3. Stay still 30 s per condition: Hard / Soft / Off occlusion (Occlusion button), hands vs controllers, during a
   spoken answer. Paste the lines into the table above with the date and build hash.

Captures (PNG/CSV) live in this folder, named `YYYY-MM-DD-<build>-<scene>.png`. See `occlusion.md` for the per-mode
cost of depth occlusion.
