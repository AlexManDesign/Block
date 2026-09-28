# R76 — CROSS-CHUNK FLUID SEAMS

Date: 2026-09-20  
Unity target: 2022.3.62f3  
Baseline: R75_LAVA_SOURCE_PARITY_2026-09-20

## Reported case

The PlayMode screenshot showed an open-water/cave boundary near `z ~= 2687.8` with a persistent dry pocket and `waterQ 0`.

`floor(2687.8) = 2687`, and `2687 mod 16 = 15`, so the observed boundary is exactly on the +Z edge of a 16x16 chunk. That is the edge case this revision targets.

## Source finding

`main.js` `EP(chunk)` seed-scans generated water/lava after chunk publication, but its horizontal AIR checks are local-array checks only:

- X- only when local X > 0;
- X+ only when local X < 15;
- Z- only when local Z > 0;
- Z+ only when local Z < 15.

Therefore a fluid cell at local X/Z 0 or 15 cannot see AIR in the adjacent chunk. If no later block edit queues that fluid, the normal 200 ms water or 500 ms lava simulator can remain idle at that seam.

R75 reproduced that source limitation.

## R76 fix

R76 keeps the existing source-style `SeedGeneratedFluidEdges()` unchanged and adds a deliberately improved seam-only pass immediately after the new column is published:

`SeedLoadedNeighborFluidSeams(ChunkColumn)`

For each already-loaded X-/X+/Z-/Z+ neighbour it scans only the shared boundary plane. For each pair of adjacent loaded cells:

- if one side is AIR and the other side is WATER/FLOW, the fluid cell is added to the existing water seed queue;
- if one side is AIR and the other side is LAVA/LAVA_FLOW, the fluid cell is added to the existing lava seed queue;
- if the neighbouring chunk is not loaded, nothing is queued — missing chunks are never treated as AIR;
- no block is changed by the seam pass itself.

Actual propagation is still performed by the existing source-derived simulators:

- water: 200 ms, 400 normal + 80 seed candidates, max 120 mutations;
- lava: 500 ms, 240 normal + 40 seed candidates, max 60 mutations;
- both seed queues keep the 20,000 entry cap and HashSet deduplication.

This means the fix does not create a new flood-fill or a second fluid solver.

## Performance scope

The pass executes only when a generated chunk is integrated, never in the render loop.

World height is 384 blocks. One seam contains `16 * 384 = 6,144` cell pairs. With all four neighbours already loaded, the absolute maximum is 24,576 pair checks for that one chunk publication. Null/null sections are skipped, and only an AIR/fluid pair performs a queue operation.

For comparison, the already-existing R75 `SeedGeneratedFluidEdges()` can scan up to `24 * 4096 = 98,304` cells per published chunk, so the new worst-case seam scan is at most 25% of that existing publication-side scan and does no mesh, light, GPU, or direct-render work.

## Why it fixes the screenshot

If the water is at local Z=15 and the dry cave cell is at local Z=0 of the +Z neighbour (or the reverse), publishing the second chunk now detects that exact AIR/fluid pair and seed-queues the water cell. On the next normal water tick, the existing FLOW3/FLOW2/FLOW1 rules can enter the dry cell.

The same protection is applied to lava seams.

## Production diff

Exactly one production file changed from R75:

- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`

No changes were made to:

- `ChunkMesher`;
- shaders/materials;
- direct chunk renderer;
- generation/noise/biomes;
- mob AI/spawning;
- player movement;
- water/lava tick rules or their budgets.

## Static validation

Selected checks after the patch:

- R76 cross-chunk fluid seam contract: 25/25 PASS
- R75 lava parity regression: 83/83 PASS
- R32 water/light regression: 31/31 PASS
- R49 current renderer/runtime regression: 27/27 PASS
- R51 survival/items/TNT regression: 71/71 PASS
- R56 mobs regression: 76/76 PASS

Total: **313/313 PASS**.

Unity Editor is not installed in this environment, so the final runtime acceptance check is to revisit the same `z=2687/2688` seam in PlayMode. `waterQ` should briefly become non-zero after the second chunk is integrated, then the previously dry exposed cells should settle using the normal water simulation.
