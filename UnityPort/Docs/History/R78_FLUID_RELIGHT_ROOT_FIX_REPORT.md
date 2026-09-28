# R78 — FLUID RELIGHT ROOT FIX

Date: 2026-09-20  
Target: Unity 2022.3.62f3  
Baseline: R76 cross-chunk fluid seams, with R77 explicitly rejected/reverted.

## Why R77 was the wrong direction

The uploaded original R74 and the first post-port capture were fast. The regression appeared after the R76 cross-chunk fluid seam change. The bad capture shows:

- FPS ~24
- CPU/GPU ~41.9 / 0.0 ms
- streaming work/budget ~34.37 / 34.35 ms
- **light phase ~34.12 ms**
- water ~0.00 ms in that sampled frame
- lava ~0.23 ms

Therefore the dominant cost is deferred lighting, not rendering/GPU and not the fluid solver itself. R77 capped the scheduler budget, but that only masked the cost. R78 removes the R77 pacing fuse and restores the exact R74/R75 source-style budget formula:

`max(sourceRefreshMs * .75, sourceRefreshMs - 5)`

The original mesh scheduling deadline reservations are also restored.

## Root cause in R76

R76 correctly detects WATER/FLOW <-> AIR contacts that lie exactly across an already-loaded chunk boundary and seed-queues the existing source-derived fluid solver.

That makes previously dormant shoreline/cave seams start normal water propagation. The water solver itself is bounded (200 ms cadence, 400+80 candidates, max 120 mutations), but each AIR->FLOW mutation produced a deferred `LightEdit`.

The Unity R32 light correctness path had a radius-16 skylight frontier shell intended for **opening** a cavity (for example STONE->AIR). Before R78, the deferred job decided to run this shell only from the *new* block being light-passable. WATER is light-passable, so AIR->WATER incorrectly ran the opening shell even though water makes skylight propagation weaker (`LightCost`: AIR=1, WATER=2).

One radius-16 shell performs:

- 128 perimeter X/Z columns * 33 Y samples = 4224 checks
- 31*31 interior X/Z * 2 cap planes = 1922 checks
- total = **6146 world-light sample checks per edit**, before propagation

A 120-mutation water tick could therefore perform up to **737,520 unnecessary shell sample checks**. This matches the F3 signature where the light phase monopolized almost the complete ~34 ms streaming deadline.

## Correct fix

Deferred `LightEdit` now carries the old/new BlockId and metadata. `VoxelLighting` classifies two independent questions from the actual source-style light semantics:

1. `RequiresSkyRelight(old,new)` — did direct-sky pass, attenuation cost, or directional sky pass change at all?
2. `RequiresSkyFrontierShell(old,new)` — did the edit make skylight access *better* (opaque->passable, lower attenuation, or a newly opened direction)?

The resumable deferred job uses those flags:

- no sky-semantic change -> skip the entire skylight half of the job;
- sky decrease/change without opening -> run direct-column/removal/propagation but **skip** the R32 shell;
- actual opening -> keep the exact R32 shell.

Expected fluid cases:

| Edit | Sky relight | R32 opening shell |
| --- | --- | --- |
| AIR -> WATER/FLOW | yes | no |
| WATER/FLOW -> AIR | yes | yes |
| AIR -> LAVA/FLOW | no | no |
| LAVA -> OBSIDIAN | yes | no |
| STONE -> AIR | yes | yes |
| AIR -> STONE | yes | no |

Block-light relighting is not skipped, so lava emission and torch/light propagation correctness are preserved.

## What remains unchanged

- R76 cross-chunk seam detection and source queues
- water cadence/bounds: 200 ms, 400+80, max 120 mutations
- lava cadence/bounds: 500 ms, 240+40, max 60 mutations
- R74/R75 frame deadline formula
- chunk renderer / ChunkMesher / shaders / direct DrawMesh path
- worldgen / biomes
- mob AI / spawning
- ship runtime
- player movement

## Static validation

- R78 root-fix contract: 35/35 PASS
- R75 lava/source parity: 83/83 PASS
- R32 water/light: 31/31 PASS
- R49 current renderer/runtime: 21/21 PASS
- R51 survival/items/TNT: 71/71 PASS
- R56 mobs: 76/76 PASS

Selected total: **317/317 PASS**.

R76's own validator reports 24/25 only because it hard-codes the R76 revision string; every functional seam check still passes. R78 has its own revision-aware seam/root-fix validator.

## Runtime acceptance

Unity Editor is not installed in this environment, so PlayMode FPS cannot be honestly claimed here. The important F3 acceptance signature on the same shoreline/cave test is:

- `light` must no longer sit near the full streaming budget during AIR->water propagation;
- `stream work` should naturally return to the R74/R75 behavior without any R77 pacing cap;
- the cross-chunk pocket should still fill because the R76 seam seed remains enabled.
