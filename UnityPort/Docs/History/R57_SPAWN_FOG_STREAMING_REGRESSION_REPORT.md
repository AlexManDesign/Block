# R57 — LAND MOB SPAWN + FOG/STREAMING REGRESSION (2026-09-19)

Target: Unity 2022.3.62f3. Canonical behavior source: uploaded `main(20260917-174448).js`.

## What was actually wrong

### 1. One-time fauna seeding was attached to the wrong pipeline stage

`main.js` calls `eu(cx,cz)` immediately after the generated chunk is published into `rA.chunks`.
R56 called `MobSpawner.OnChunkLoaded()` only after startup/runtime cross-chunk lighting completed.
That made fauna lifecycle depend on rendering-pipeline progress and changed the RNG/event order relative to the source.

R57 moves the call into `VoxelWorld.IntegrateGenerated()` immediately after `chunks[c.Coord]=c`.
The calls from startup StitchLight and runtime ProcessChunkLighting are removed.

Spawn probabilities and rules were NOT increased:
- initial passive opportunity remains 10% per newly seeded chunk;
- natural passive check remains every 20 s during source daytime;
- passive support remains exactly GRASS;
- Xp-style vertical scan remains player Y +/-20 with 2/3 free cells based on mob height;
- hostile/slime light and biome rules are unchanged.

F3 now exposes cumulative land spawn attempts/successes, unavailable/no-candidate rejection counts,
and source one-time seeded chunk/passive-roll/passive-group counts. This is diagnostic only and consumes no extra RNG.

### 2. Fog was using the target render distance before that render square existed on the GPU

The source startup gate initially meshes radius `min(RENDER_R,3)`. Unity correctly retained this behavior.
With target RD=6 the source fog formula gives near/far = 70/94 blocks, but immediately after the gate only
radius 3 (~48 blocks) may have published GPU geometry. The result is the hard blue world edge visible in the
user screenshot: geometry ends before fog has even started.

R57 adds an event-driven `PublishedRenderRadius`: the largest contiguous Chebyshev ring around the player's
current chunk whose chunks have drawable GPU meshes. It is recalculated only when publication state can change
(GPU upload/drop, center move, render-distance change), never as a per-frame cache scan.

During streaming only, fog uses `min(configured RD, published RD)`. It does NOT reduce generation, culling,
render target distance, biome fidelity or mesh quality. Once the target square is published the uniforms become
exactly the original main.js formula again:
- normal fogNear = RENDER_R*16 - 26
- normal fogFar  = RENDER_R*16 - 2
- underwater remains 4 / 18

For the screenshot case target=6 / published=3, transient fog is 22/46 blocks, so it blends before the ~48-block
published edge instead of showing an unfogged void. When radius 6 is ready it automatically returns to 70/94.

### 3. Microstutter regression check

R55 -> R56 hashes confirm these core systems were unchanged:
- `VoxelWorld.cs`
- `VoxelLighting.cs`
- `ChunkMesher.cs`
- `ChunkGenerator.cs`

So R56 did not reintroduce an old world-generation/light/mesh algorithm. R56 did add the skeleton item-atlas
entity pass. R57 measures the complete entity batch rebuild+native upload instead of imposing a throttle:
- F3: `entity <last> max <peak> ms`
- spike attribution: `[entity-batch Xms]`

This allows a real PlayMode capture to distinguish fish/entity cost from chunk-light, mesh-upload, water,
save, AI and general streaming.

## Production changes R56 -> R57

- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`
- `Assets/BlockcraftPort/Rendering/VoxelRenderEnvironment.cs`
- `Assets/BlockcraftPort/AI/MobSpawner.cs`
- `Assets/BlockcraftPort/Rendering/MainEntityBatchRenderer.cs`
- `Assets/BlockcraftPort/Rendering/MainPerformanceHud.cs`

No ChunkMesher, VoxelLighting, ChunkGenerator, MobAI or voxel shader rewrite was made.

## Validation

Current compatible source/regression suites:
- R29 mob collision: 109/109 PASS
- R30 mob movement exact: 132/132 PASS
- R31 mob behavior exact: 73/73 PASS
- R32 water/light (updated only to recognize the R54 transactional implementation): 31/31 PASS
- R49 current renderer: 30/30 PASS
- R51 survival/items/TNT: 71/71 PASS
- R52 static chests/furnace/crafting: 87/87 PASS
- R52.1 compile-hotfix guards: 8/8 PASS
- R52.2 scene-setup guards: 9/9 PASS
- R54 architecture: 63/63 PASS
- R56 mobs: 76/76 PASS
- R57 spawn/fog regression: 47/47 PASS

Total: 736/736 PASS across the selected compatible matrix.

The environment used to build this archive does not contain Unity Editor/Roslyn, therefore actual PlayMode frame
timing is not claimed as measured here. R57 intentionally adds the missing runtime evidence needed for that check.
