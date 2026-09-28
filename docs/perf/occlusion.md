# Depth occlusion — cost per mode (Quest 3, passthrough on, 72 Hz)

Real furniture hides the machine using the headset's environment depth (AR Foundation `AROcclusionManager` +
`ARShaderOcclusion`). Every machine material uses `Fieldmate/OccludedLit`, which reads the depth texture per pixel.

| Mode | What runs | Edges |
|---|---|---|
| Hard | Environment depth (Fastest) + 1 depth sample per pixel, `clip()` | Stair-stepped at depth-texture resolution |
| Soft | Environment depth (Fastest) + 5 depth samples per pixel, coverage written as alpha (passthrough shows through) | Faded over ~1.5 texels and a 6 cm depth band |
| Off | Nothing: depth provider stopped | Machine always drawn on top |

Soft deliberately does **not** use AR Foundation's `SoftOcclusion` shader mode, which adds a full-screen depth
preprocessing pass every frame; the edge fade is done in our shader from the raw depth texture instead.

## How to measure

1. Build and install (`tools/build.sh`, `tools/install.sh`), open the bench scene, place the machine.
2. Stand about 1.5 m in front of it with a chair or table partly in front of the machine.
3. The **Occlusion** button under the Start button cycles Hard → Soft → Off; the mode is kept between launches.
4. Stay in each mode for at least 30 s without moving much. `FrameTimeProbe` logs a line every 5 s:

   ```
   adb logcat -s Unity | grep "\[Perf\]"
   [Perf] occlusion=Hard cpu 7.9 ms gpu 9.1 ms frame 13.9 ms (360 frames)
   ```

   `cpu`/`gpu` come from Unity's `FrameTimingManager` (Frame Timing Stats is on in Player settings); `frame` is the
   display interval (13.9 ms at 72 Hz while the app keeps up). Cross-check with OVR Metrics Tool (#18).
5. Take the median of the logged windows per mode and fill the table.

## Results

| Mode | CPU ms | GPU ms | Frame ms | Δ GPU vs Off | Date, build |
|---|---|---|---|---|---|
| Off | | | | — | |
| Hard | | | | | |
| Soft | | | | | |

Not measured yet: requires the device (added with #6). Budget: app frame time ≤ 11 ms (see `budget.md`).
