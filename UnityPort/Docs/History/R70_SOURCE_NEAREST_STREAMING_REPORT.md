# R70 — source-nearest streaming + fog audit

Revision: `R70_SOURCE_NEAREST_STREAMING_2026-09-20`
Target: Unity `2022.3.62f3`
Canonical reference: supplied `main(20260919-202006).js`.

## Why R69 looked wrong at Render Distance 20

The supplied runtime screenshot showed approximately:

- FPS ~174.5, p95 ~10.2 ms, p99 ~13.4 ms, max ~20.4 ms.
- CPU/GPU ~4.1 / 2.6 ms.
- `rd target/gpu-ready 20/4`.
- source fog `294..318`.
- `chunks 211`, `gpu 147`, `meshSec ~470`, generation request window saturated at 10.

The renderer was not GPU-bound. The visible problem was that the requested source fog band was hundreds of metres away while the contiguous GPU-ready terrain ring was only about four chunks (~64 m). There was therefore no terrain at 294–318 m for the fog shader to blend. The blue void was caused by terrain streaming lag, not a fog-distance formula error.

At source desktop maximum RD=20, the visible square contains up to 41×41 = 1681 chunk coordinates and generation radius 21 covers up to 43×43 = 1849 chunk coordinates. Changing from RD=6 to RD=20 therefore creates a very large catch-up workload; it is not expected to become fully GPU-ready in one frame.

## Fog audit against main.js

Canonical source settings:

- default `RENDER_R=6`, `GEN_R=7`;
- presets `[4,6,8,12,16,20,25,30,40,50]`;
- device cap: 8 on <=2 GB, 12 on <=4 GB/mobile, otherwise 20;
- `GEN_R = RENDER_R + 1`;
- far plane `max(420, RENDER_R*16*2)`.

Canonical source fog:

- air near = `RENDER_R*16 - 26`;
- air far = `RENDER_R*16 - 2`;
- underwater = `4..18`;
- shader fade is linear in Euclidean world-space camera distance.

Therefore RD=20 must produce `294..318`. Unity already matches these exact equations and R70 does not change the fog implementation.

The fog band is only 24 m wide and is located at the very edge of the source render distance. When the terrain ring is fully ready it should look like a subtle fade into the sky near the horizon, not like volumetric haze over the whole scene. If F3 eventually shows `gpu-ready 20` and terrain at ~300 m still does not fade, that would be a separate shader/render bug. The supplied screenshot did not reach that condition.

## R69 scheduler review

The R69 techniques were technically valid streaming techniques, but they were not the canonical main.js algorithm:

- camera-facing priority;
- side/back tiers;
- velocity/predictive lead;
- adaptive extra worker slot based on frame pacing.

They were added to compensate for Unity throughput, but together they made the scheduler harder to reason about. R70 removes them from actual scheduling rather than stacking more heuristics on top.

## R70 streaming algorithm

R70 follows the canonical source order:

1. Generation candidates are in the real `GEN_R` square around the current player chunk.
2. Generation pending work is bounded to 10 requests (`WT=10`).
3. Pending generation is chosen by squared chunk distance to the current player chunk only.
4. Mesh candidates are inside the real `RENDER_R` square only.
5. A chunk is meshable only after its complete 3×3 neighbourhood exists/has crossed the lighting barrier.
6. Mesh candidates are selected by squared chunk distance only.
7. At most three new mesh chunk jobs are submitted per scheduler pass, matching source `ol()`.
8. No camera cone, no predictive center and no adaptive BOOST alter queue ordering.

The only intentional platform adaptation is worker width. main.js creates the same `GI=clamp(hardwareConcurrency-1,2..4)` count for both WebWorker pools. Copying that literally to native Unity previously caused CPU contention on low-core machines because Unity also has main/render/driver threads. R70 therefore keeps fixed symmetric pools but reserves CPU on low-core hosts:

- 4 logical CPUs -> 2 generation + 2 mesh;
- 6 -> 3 + 3;
- 8+ -> up to 4 + 4.

Threads stay `BelowNormal`. This is a fixed platform adaptation, not a frame-time-dependent boost.

## F3 changes

The F3 line now reports `ready~Xm`, where X is the current contiguous GPU-ready radius in metres, and labels pending mesh work as `meshSec` because the Unity queue counts sections rather than source chunk jobs.

Example at RD=20 while still catching up:

`rd target/gpu-ready 20/4  fog 294..318 SRC  ready~64m ...`

This makes the distinction explicit: fog target can be correct while terrain has not reached it yet.

## Files changed from R69

Production changes are limited to:

- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`
- `Assets/BlockcraftPort/Rendering/MainPerformanceHud.cs`

Fog implementation, shaders, direct renderer, ChunkMesher, ChunkRender, world generation, biomes, mob AI, mob spawn, player controller and block lifecycle are byte-identical to R69.

## Static validation

- R70 source-nearest contract: 50/50 PASS.
- R32 water/light: 31/31 PASS.
- R51 survival/items/TNT: 71/71 PASS.
- R56 mobs/render lifecycle: 76/76 PASS.
- Selected checks: 228/228 PASS (sets overlap conceptually).

These are static/source-contract checks. This environment has no Unity Editor/Player runtime, so actual RD=20 catch-up speed and frame-time impact must still be verified in Play Mode on the target PC.
