# R74 — source water semantics + lighting frame pacing

## What the latest screenshot actually shows

The latest F3 capture is CPU-bound, not GPU-bound:
- FPS ~85.8
- p95 34.3 ms, p99 62.8 ms, max 111.6 ms
- CPU/GPU ~18.4 / 3.0 ms
- streaming ~8.97 / 8.92 ms
- chunk-light stitch ~8.95 ms
- water 0.00 ms, waterQ 0

So water was not causing the FPS regression in that capture. The persistent per-frame cost was the cross-chunk lighting stitch consuming almost the full streaming budget.

## Correction to the R73 diagnosis

The previous statement that the deepslate cave-water generation flood pass was missing was wrong. R73 already contains `ChunkGenerator.SeaFloodAndFreeze()` after cave/aquifer carving. Its structure matches the `genWorker.js` deepslate pass: seed existing WATER and low columns below sea level, then flood through AIR in six directions inside the chunk.

The remaining water differences were in runtime `main.js fE()` semantics, not the presence of the generation flood pass.

## Water parity changes

R74 ports the remaining high-impact `fE()` predicates used by main.js:

- Aquatic blocks count as source-water carriers when `needsWater` is true or metadata bit 4 is set (`Iu`/`g1`).
- FLOW support/source tests now use those water carriers rather than only literal WATER.
- Fluid can replace source cross vegetation (but not aquatic vegetation), instead of only AIR.
- Fluid support now follows source `Ae()` semantics rather than renderer `IsFullOpaque()`. This matters because render occlusion and fluid support are not the same concept.
- Existing source cadence/bounds remain unchanged: 200 ms water tick, 400 normal + 80 seed candidates, max 120 mutations.
- R73's EP-equivalent remains attached to every chunk publication, including preload.

No global cave flood heuristic was added to runtime water.

## FPS/frame-pacing changes

`main.js Yu()` limits its border-light seed scan with `U4()` to the top of the highest allocated section in the relevant surrounding chunks. R73's Unity stitch continuation scanned every border column all the way from Y=-64 to Y=319, which is extra work.

R74 now:
- computes the source-equivalent highest loaded stitch Y from the surrounding 3x3 chunk area;
- scans only through that Y during the border seed stage;
- preserves the exact persistent propagation queues/results;
- gives runtime cross-chunk lighting its own cooperative slice: 2.0 ms desktop / 1.25 ms mobile, rather than allowing one stitch continuation to consume the whole ~9 ms streaming budget.

This scheduling cap does not discard lighting work; it only resumes the same job next frame.

## F3 cave diagnostic

F3 now displays:
- exact player xyz;
- source sea level;
- block at feet;
- block below feet.

This is necessary to distinguish an actual below-sea dry AIR defect from a normal cave ledge/air volume above the water surface. The next screenshot from the same pocket will therefore be conclusive instead of relying on perspective.

## Files changed from R73

Exactly four production files:
- `Core/BlockRegistry.cs`
- `Rendering/VoxelLighting.cs`
- `Rendering/VoxelWorld.cs`
- `Rendering/MainPerformanceHud.cs`

Byte-locked unchanged: ChunkGenerator, ChunkMesher, direct renderer, ChunkRender, MobAI, MobSpawner, ship runtime and fog environment.

## Important audit backlog after R74

Next correctness work remains:
1. Full source lava simulation: LAVA_FLOW2/LAVA_FLOW1, 500 ms queue/seed tick, water-contact obsidian/cobble.
2. Remaining aquatic/waterlogged gameplay semantics outside the water tick.
3. Carrot/potato and planted pumpkin/melon lifecycle.
4. Minimap.
5. Authoritative armor/offhand inventory/save/UI.
6. Mods ownership/UI and sound layer.

Ship development remains intentionally out of scope per user request.

## Validation

`Tools/validate_r74_water_semantics_light_frame_pacing.py`: 37/37 PASS.
Existing R32 water/light structural regression: 31/31 PASS.

Unity Editor is not installed in the execution environment, so runtime FPS/PlayMode must be verified in the user's Unity build.
