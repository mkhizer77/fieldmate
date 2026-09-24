# Performance budget — Quest 3, passthrough on, 72 Hz

Targets from docs/design.md §6. Fill the "Measured" column from OVR Metrics Tool / the in-app overlay.

| Item | Budget | Measured (date, build) |
|---|---|---|
| App frame time | ≤ 11 ms | |
| Draw calls | ≤ 120 | |
| Triangles | ≤ 400 k | |
| Texture memory | ≤ 300 MB | |
| GC alloc / frame (interaction + telemetry) | 0 | |

Captures live in this folder as PNG/CSV, named `YYYY-MM-DD-<build>-<scene>.png`.
