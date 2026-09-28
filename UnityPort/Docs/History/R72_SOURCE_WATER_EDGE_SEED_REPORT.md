# R72 — SOURCE WATER EDGE SEED

Date: 2026-09-20
Unity: 2022.3.62f3
Baseline: R71_SOURCE_NEAREST_CLEAN_2026-09-20

## Reported symptom

Generated cave/ocean water can sit directly beside real AIR cells, leaving a genuinely dry pocket. The F3 capture showed `waterQ 0` while such a water/air boundary was visible.

## Root cause

`main.js` steady-state chunk integration publishes a generated chunk and then calls `EP(chunk)`. `EP` scans generated WATER/FLOW cells and seed-queues cells that are exposed to AIR below or horizontally. The normal `fE()` water simulation then settles those boundaries on its 200 ms cadence.

R71 had the runtime `fE()`-equivalent and edit-neighbour queuing, but generated chunk integration never performed the `EP()` seed scan. Generated fluid could therefore remain completely inert unless a later player/world edit happened to queue it.

This is a missing source stage, not a water shader/mesh problem.

## R72 fix

Added `VoxelWorld.SeedGeneratedWaterEdges(ChunkColumn)` and call it on steady-state generated-chunk publication, matching source integration ordering.

The scan mirrors the water half of `main.js EP()`:

- scans all non-null chunk sections;
- accepts WATER/FLOW3/FLOW2/FLOW1;
- checks AIR below, X-/X+, Z-/Z+ inside the newly published chunk using the same dense-index layout;
- queues exposed fluid through the existing seed queue (`EnqueueWater(..., true)`);
- does not directly mutate AIR into WATER;
- leaves the existing `fE()`-parity simulation to perform FLOW3/FLOW2/FLOW1 propagation;
- keeps the source queue cap 20,000, pull 400 normal + 80 seed, mutation cap 120, tick period 200 ms.

The source startup preload does not call `EP()` on its preload integration path, so R72 also only runs this scan once `InitialWorldReady` is true. This avoids inventing extra initial-world behavior.

## Why this addresses the screenshot

With a generated source-water cell next to cave AIR and solid/water support below, `EP()` places the water cell into the seed queue. On the next source water tick, the existing flow rule can place FLOW3 into the adjacent AIR. In R71 the queue could remain empty forever, which matches the observed `waterQ 0` dry boundary.

## Scope

Production code changed from R71: exactly one file:

- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`

No changes to:

- water mesh/shader;
- fog;
- renderer;
- chunk mesher;
- generation/noise/biomes;
- mob AI;
- F flight / render-distance hotkeys;
- source-nearest streaming scheduler.

## Validation

- R72 source water edge seed contract: 26/26 PASS
- R32 water/light regression: 31/31 PASS
- delimiter/static integrity: PASS

The R71 validator reports only its expected revision-name check as different because the project revision is now R72; all 22 behavioural/cleanup checks after that remain PASS.

Unity Editor is not available in this environment, so the final runtime verification is the same cave boundary in PlayMode: after its chunk streams in, `waterQ` should become nonzero briefly and the exposed dry AIR cells should receive normal source-style flowing water instead of remaining permanently dry.
