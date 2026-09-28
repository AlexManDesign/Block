# R65 — Uncapped desktop FPS + smooth native worker scheduling

Target: Unity 2022.3.62f3  
Canonical behavior source: `main(20260919-202006).js`

## What the screenshot exposed

The reported frame is not geometry-heavy: 57 visible chunks, 107 chunk draw calls, about 484k triangles, GC0 +0, mesh upload max about 0.42 ms and save-capture max about 0.16 ms. The same capture shows `workers g/m 3/3`, a mesh backlog of 176 and a recorded mob-AI peak of about 60.70 ms.

Two Unity-port regressions were confirmed in code:

1. R59-R64 disabled VSync but then set `Application.targetFrameRate` to the display refresh rate. On a 60 Hz display this is still a hard 60 FPS cap even though `QualitySettings.vSyncCount == 0`.
2. R62 copied the browser WebWorker pool width literally into *both* native Unity pools. On a 4-logical-thread CPU this creates 3 generation threads + 3 mesh threads in addition to Unity's main/render/driver work. That can pre-empt the frame and turn otherwise cheap AI/render work into large wall-clock spikes.

## Fog: source port vs workaround

The source fog itself is simple and stable:

- air near = `RENDER_R * 16 - 26`
- air far = `RENDER_R * 16 - 2`
- underwater = `4 .. 18`

R62 was the mistake: it made fog depend on the discrete GPU-ready chunk ring as a safety workaround. That produced roughly one-chunk (16 block) horizon jumps. R64 removed that coupling completely and restored the source formula. R65 keeps the R64 source-exact fog unchanged.

Predictive streaming is separate from fog. It only changes which future chunks generation/mesh/upload prepare first; it does not change visible render distance or fog values.

## R65 changes

### 1. Desktop/Editor is truly uncapped

`PortBootstrap` now keeps:

- `QualitySettings.vSyncCount = 0`
- `Application.targetFrameRate = -1` on desktop/Editor

Mobile still receives a bounded display-refresh target to avoid runaway thermal load.

The WebGL source uses `requestAnimationFrame`, so the browser build is normally display-paced. That timing mechanism is not a gameplay rule and should not be re-created as an artificial Unity desktop cap.

### 2. Native worker width reserves frame threads

The generation and mesh algorithms/queues are unchanged, but their *native execution width* is now adapted to Unity:

`workerCount = clamp((logicalCores - 2) / 2, 1, 4)` per heavy pool.

Examples:

- 4 logical CPUs: 1 gen + 1 mesh
- 6 logical CPUs: 2 + 2
- 8 logical CPUs: 3 + 3
- 12+ logical CPUs: 4 + 4

Workers remain `BelowNormal` priority. This leaves capacity for Unity main/render/driver threads instead of saturating a 4-thread host with six heavy background workers.

### 3. Predictive flight feed reduced to one chunk

R64 could shift the predictive center by two chunks. At the source maximum flight speed (16 blocks/s), the 0.75–1.25 s forecast only needs the next chunk boundary. R65 clamps the prefetch center to ±1 chunk in each horizontal axis, avoiding a second full edge strip being queued while the first is still meshing.

Visible chunks and direct-render distance are unchanged.

### 4. F3 can identify CPU vs GPU limit

While F3 is visible, the overlay now reports:

- `CPU/GPU x/y ms`
- `cap uncapped` (or the active numeric cap)

This is diagnostic-only and is not sampled while F3 is hidden.

## What was deliberately not changed

Byte-identical to R64:

- `MainChunkDirectRenderer.cs`
- `ChunkMesher.cs`
- `ChunkRender.cs`
- `VoxelLighting.cs`
- `VoxelRenderEnvironment.cs` (therefore fog shaders/state are untouched)
- world voxel shaders
- `ChunkGenerator.cs` / `BiomeGenerator.cs`
- `MobAI.cs` / `MobSpawner.cs`
- ship runtime/render/shaders

The recorded `ai max 60.70 ms` is therefore not hidden by an AI rewrite in this revision. Worker oversubscription could inflate that wall-clock duration through CPU contention; R65 removes that confirmed contention first. If a fresh R65 F3 capture still shows isolated AI spikes with low GPU time, that can then be optimized inside the source-exact A* implementation without mixing two regressions in one patch.

## Validation

Current R65 contract: **59/59 PASS**.

Additional applicable contracts:

- R32 water/light: **31/31 PASS**
- R56 mobs: **76/76 PASS**
- R60 render/mod/gameplay: **80/80 PASS**
- R61 block lifecycle/no-ship: **105/105 PASS**

Some much older renderer validators encode architectures that were intentionally superseded in later revisions (for example old pass ordering, ship exclusion, or old HUD strings), so their historical assertions are not used as a combined pass count for R65.

Unity Editor is not installed in this execution environment, therefore the actual FPS and CPU/GPU frame timings must be measured in your Unity 2022.3.62f3 Play Mode/build. R65 adds the exact counters needed for that runtime check.
