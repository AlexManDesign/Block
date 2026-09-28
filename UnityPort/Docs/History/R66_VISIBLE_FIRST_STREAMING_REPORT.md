# R66 — Visible-first streaming / stable source fog

Target: Unity 2022.3.62f3. Baseline: R65_UNCAPPED_SMOOTH_WORKERS.

## Runtime evidence from the supplied F3 capture

The supplied capture is no longer frame-rate limited:

- FPS: 181.1
- p95: 8.7 ms
- p99: 10.2 ms
- rolling max: 16.9 ms
- CPU/GPU: 4.7 / 1.9 ms
- cap: uncapped
- GC0: +1
- 23 visible chunks, 42 draw calls, about 109k triangles
- target render distance / contiguous GPU-ready radius: 6 / 2
- fog: 70..94, SRC
- workers: gen 1 / mesh 1
- generation queue: 10
- mesh queue: 262
- upload/light/stitch queues: 0 at the captured instant

This shows that the render/GPU path has large headroom. The visual horizon problem is not the fog formula: the fog correctly starts at 70 and ends at 94 for RD=6, but the contiguous published terrain radius is only 2 chunks (~32 blocks). Geometry therefore ends before the fog band can hide it.

## Root cause

R64/R65 predictive streaming changed worker priority toward the forecast center. With the R65 conservative 1 gen + 1 mesh configuration on a 4-thread host, speculative work could occupy the bounded queues while chunks in the *current visible* RENDER_R square were still missing. The F3 capture (`6/2`, `mesh 262`) demonstrates this starvation.

The fog itself remains the exact main.js rule and is not changed in R66:

- air near = `RENDER_R * 16 - 26`
- air far = `RENDER_R * 16 - 2`
- underwater = `4 .. 18`

## R66 fix

R66 fixes the producer side instead of moving the fog:

1. Current visible generation is always tier 0. Predictive generation is tier 1 and receives only spare slots from the bounded generation window.
2. Current visible mesh work is always tier 0. Prefetch-only chunks can never jump ahead of a missing visible chunk.
3. Native worker split is asymmetric because the runtime capture shows meshing, not generation, is the bottleneck. On a 4-thread host the split is now 1 generator + 2 mesh workers. Larger hosts scale to 1+3 or 2+4 while retaining BelowNormal worker priority.
4. Predictive lookahead remains bounded to one chunk. It never expands draw distance and never changes fog values.
5. Renderer, mesher algorithm, lighting, shaders, fog environment, world generator, biome generator, AI and ship files are byte-identical to R65. Production diff is only `Rendering/VoxelWorld.cs`.

## Expected runtime result

With RD=6, F3 should still show `fog 70..94 SRC`. The important change is that `gpu-ready` should climb toward 6 and the `mesh` backlog should drain materially faster. The horizon should therefore be covered by geometry before entering the fog band, instead of exposing a sharp chunk edge against the sky.

FPS should remain well above 60 because R65 uncapping is retained. The supplied R65 capture already proves the GPU is not the bottleneck (1.9 ms GPU) and the main CPU frame is ~4.7 ms.

## Static validation

`validate_r66_visible_first_streaming.py`: 62/62 PASS.

Also passed without changes to their contracts:

- R32 water/light: 31/31
- R56 mobs: 76/76
- R60 render/mod/gameplay: 80/80
- R61 block lifecycle: 105/105

Legacy validators whose purpose is byte-locking an older VoxelWorld revision are intentionally not counted after R66 changes the streaming scheduler. R63's thread-safety invariants are separately rechecked by the R66 contract.

Unity Editor/Player runtime cannot be executed in this environment, so the final proof of queue drain and horizon coverage is the user's next F3 capture.
