# R64 — Stable fog + predictive flight streaming

Target: Unity 2022.3.62f3  
Canonical behavior source: `main(20260919-202006).js`

## What was wrong

R62 made fog depend on `PublishedRenderRadius`. That value advances in whole chunk rings, so a one-ring change moved the fog horizon by about 16 blocks at once. During fast flight this caused the reported far/near popping.

The source `main.js` does not do that. Its air fog remains exactly:

- near = `RENDER_R * 16 - 26`
- far = `RENDER_R * 16 - 2`
- underwater = `4 .. 18`

## R64 fix

Fog is again completely independent of generation, meshing, upload queues, and GPU-ready radius. There is no adaptive/safety fog state and no fog smoothing heuristic.

Instead, flight latency is handled in the streaming pipeline itself:

1. Read the real horizontal player velocity while creative-flying.
2. Predict the future position 0.75–1.25 seconds ahead, scaled by speed.
3. Clamp the prediction to at most two chunks in X/Z.
4. Re-prioritize pending generation around that forecast center.
5. Feed the forecast generation window first, then the live-center window as fallback.
6. Allow the forecast square into mesh and GPU-upload work, so upcoming rows are prepared before crossing the chunk boundary.
7. Keep direct rendering strictly centered on the real player and exactly at `RenderDistance`; predictive streaming does not increase visible distance or draw calls.

At the source maximum flight speed of 16 blocks/s, the forecast covers roughly 12–20 blocks, naturally one or two chunks depending on the player's position inside the current chunk.

## Important invariants

- `GenerationDistance = RenderDistance + 1` remains unchanged.
- Source pending generation window remains 10.
- Worker count remains `max(2, min(4, processorCount - 1))` for both generation and mesh pools.
- Direct renderer, `ChunkMesher`, `ChunkRender`, `VoxelLighting`, biome generation, mob AI/spawning, shaders, and ship files are byte-identical to R63.
- R63 saved-world lifecycle thread-safety invariants remain present.
- Production diff is exactly three files: `VoxelWorld.cs`, `VoxelRenderEnvironment.cs`, `MainPerformanceHud.cs`.

## F3 diagnostics

F3 now shows stable source fog and `lead X,Z`. `lead 0,0` means source-style live-center streaming; `lead +1,0`, `+2,0`, etc. means upcoming chunks are being prepared ahead of flight. There is no `SAFE` fog mode anymore.

## Validation

Applicable selected static/source regression: **486/486 PASS**.

This includes water/light, survival/items/TNT, dry spawn, mobs, R60 gameplay/mod parity, R61 lifecycle, and the new R64 streaming/fog contract. The old R63 byte-lock assertion is intentionally superseded because R64 must change three streaming/fog files; R63 thread-safety behavior is explicitly re-checked in the R64 validator.

Unity Editor/PlayMode is not available in this execution environment, so runtime FPS/visual capture still requires opening the project in Unity 2022.3.62f3.
