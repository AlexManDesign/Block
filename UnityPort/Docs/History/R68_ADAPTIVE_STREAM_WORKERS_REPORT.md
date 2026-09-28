# R68 — Adaptive stream workers

## Runtime evidence that motivated this pass
User F3 capture on R67: ~211 FPS, p95 7.8 ms, p99 10.5 ms, max 11.7 ms, CPU/GPU about 4.0/1.5 ms, no new >=25 ms spikes. Rendering itself is no longer the bottleneck. At the same time the capture showed render distance 6 versus GPU-ready radius 2, generation queue saturated at 10 and mesh backlog around 338 sections, while upload/light/stitch queues were at zero.

## Change
R67's conservative baseline worker split is retained. R68 starts at the same baseline, but prepares one extra low-priority generation and mesh thread. Only one extra background slot may be active at a time.

When frame time is <=10.5 ms, the EWMA refresh is <=9.5 ms and streaming backlog is high, the spare slot is enabled. If generation's bounded queue is saturated, the spare slot goes to generation first because mesh capture requires a generated+lit 3x3 neighbourhood. Once generation catches up, the spare slot may switch to meshing when section backlog is >=128.

Any frame >=14 ms or sustained refresh >=11.5 ms immediately drops back to the R67 baseline and starts a 0.45 s cooldown. Burst-only threads run at ThreadPriority.Lowest; baseline workers remain BelowNormal.

This changes completion latency/order only. It does not change chunk contents, biome generation, mesh topology, lighting, fog, render distance, draw submission, AI, saves or player movement.

## Diagnostics
F3 now displays `workers g/m CURRENT base BASE BOOST`. `BOOST` appears only while the elastic slot is active. On a four-logical-thread host the expected baseline remains 1 gen + 2 mesh. Under smooth backlog pressure R68 can temporarily use either 2 gen + 2 mesh or 1 gen + 3 mesh, never both extra workers at once.

## Byte/diff scope
Production diff from R67 is exactly two files:
- `Rendering/VoxelWorld.cs`
- `Rendering/MainPerformanceHud.cs`

`ChunkMesher`, `ChunkRender`, `MainChunkDirectRenderer`, `VoxelRenderEnvironment`, shaders, lighting, generator, biomes, AI, entity renderer, player/F flight, lifecycle and save code are byte-identical to R67.

## Static regression
- R68 adaptive worker contract: 33/33 PASS
- R32 water/light: 31/31 PASS
- R51 survival/items/TNT: 71/71 PASS
- R55 spawn parity: 58/58 PASS
- R56 mobs/render: 76/76 PASS

Selected total: 269/269 PASS. These suites overlap and are not a count of unique assertions.

Unity Editor/PlayMode is not available in the build environment, so the actual effect on GPU-ready radius and backlog must be confirmed with the same F3 flight test on the user's machine.
