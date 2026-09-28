# R67 — Frame pacing + F creative flight

Target: Unity 2022.3.62f3. Baseline: R66_VISIBLE_FIRST_STREAMING.

## Evidence from the supplied R66 F3 capture

The capture shows that raw render throughput is already healthy:

- average FPS: ~158.5
- CPU / GPU: ~8.0 / 2.3 ms
- p95: ~16.2 ms
- p99: ~24.2 ms
- rolling max: ~47.6 ms
- frame cap: uncapped
- visible chunks: ~46
- draw calls: ~92
- triangles: ~398k
- target / contiguous GPU-ready radius: 6 / 4
- fog: 70..94 SRC
- generation queue: ~10
- mesh queue: ~246
- last stream work: ~0.95 ms, recorded max ~5.76 ms
- save capture: ~9.01 ms in the capture
- AI recorded max: ~25.90 ms
- entity recorded max: ~25.88 ms
- last >=25 ms spike was tagged with chunk-light (~5.1 ms)

The GPU has large headroom, so reducing draw calls or triangle count would not attack the p99/max spikes seen here. R67 therefore keeps the direct chunk renderer, chunk mesher, shaders, fog and R66 visible-first streaming byte-identical, and targets periodic main-thread CPU work instead.

## F flight

`F` is now an immediate flight toggle in Creative mode. Pressing it again disables flight. Vertical velocity is cleared on the transition so the toggle does not inherit a jump/fall impulse.

The existing source-style double-Space flight toggle is retained and both paths use the same state transition. Survival still forcibly disables flight. F5 camera mode is unchanged.

This is a requested Unity convenience binding, not a claim of source parity: the supplied canonical `main.js` has the double-Space flight control and no `KeyF` binding.

## Frame-pacing changes

### 1. Mob A* keeps source results but avoids repeated collision probes

The source search contract is unchanged: 320 expansions, ±22-cell search radius, 0.6 s repath cadence and the same stand-cell scan. R67 changes only temporary lookup implementation:

- `Vector2Int` dictionary keys are replaced with packed 64-bit integer keys;
- repeated exact `SourcePointCollision` clear/stand queries are memoized for the duration of one synchronous A* build;
- caches are cleared for every path build, so no result survives a world change.

This is intended to reduce the occasional ~25 ms AI path spike without changing which path is accepted.

### 2. Entity batch updates stop rebuilding immutable index topology

Mob box/index topology does not change when a mob merely walks, animates or receives new lighting. If the same first-person visible entity identity set remains visible, R67:

- rebuilds and uploads the animated vertex data as before;
- reuses the existing mob and held-item index topology;
- skips duplicate index generation and duplicate index-buffer uploads;
- prewarms immutable atlas UV arrays during geometry warmup rather than allocating them during gameplay.

Third-person player rendering keeps the conservative full-topology path because player-held-item topology can change independently.

### 3. Chunk-light continuation observes the shared frame deadline more closely

The world-stitch lighting algorithm, traversal order and final light values are unchanged. Deadline checks are simply made every 32 inner operations instead of much coarser intervals. This reduces the amount by which one cooperative lighting continuation can overrun the same shared stream deadline used by generation/light/mesh processing.

There is no node cap and no lighting quality reduction.

### 4. Autosave mob capture removes one temporary list + copy

`CapturePersistentMobsSource()` now counts, allocates the exact result array once and fills it in the same active-then-stored ordering. The previous dynamic `List<MainMobSaveData>` plus final `ToArray()` copy is removed.

This does not make the whole save capture free; the screenshot's ~9 ms save capture includes other world state too. It removes one avoidable allocation/copy from that path without changing save data.

## Deliberately unchanged

R67 byte-locks these performance-sensitive/source-parity systems to R66:

- `PortBootstrap.cs` (uncapped R65 policy retained)
- `VoxelRenderEnvironment.cs` (source-exact fog retained)
- direct chunk renderer
- chunk mesher / chunk render upload format
- opaque/transparent/water shaders
- chunk generator and biome generator
- block lifecycle
- R66 visible-first streaming and worker split
- all ship runtime/render/template/shader files

The fog remains independent of GPU-ready radius: air uses `RD*16-26 .. RD*16-2`, underwater uses `4 .. 18`.

## Validation

- R67 frame-pacing/F-flight contract: 70/70 PASS
- R32 water/light: 31/31 PASS
- R51 survival/items/TNT: 71/71 PASS
- R55 dry-spawn parity: 58/58 PASS
- R56 mobs/render parity: 76/76 PASS

Selected checks total: 306/306 PASS. These suites overlap, so this is a regression-check count, not a count of 306 independent features.

Unity Editor/Player cannot be launched in this environment. Therefore R67 does not claim a measured runtime p99 improvement here. The next in-Editor F3 capture should be compared primarily on p95/p99/max, `ai max`, `entity max`, the tag on >=25 ms spikes, GC0, save capture and mesh backlog.
