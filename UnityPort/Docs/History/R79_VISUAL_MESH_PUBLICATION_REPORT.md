# R79 — VISIBLE MESH PUBLICATION / VISUAL HOLE ROOT FIX

Date: 2026-09-20  
Target: Unity 2022.3.62f3  
Baseline: R78 fluid-relight root fix. R77 frame-pacing fuse remains rejected and is not present.

## Runtime evidence from the supplied capture

The supplied R78 capture is no longer lighting-bound:

- FPS ~146
- CPU/GPU ~10.2 / 3.6 ms
- streaming ~0.02 / 5.26 ms
- water/lava/light phases sampled at ~0 ms
- target render distance = 20 chunks
- contiguous GPU-ready radius = only 3 chunks
- loaded chunk data = 554 columns, GPU-published chunk meshes = 137

The large black shapes are visually identical to the clear/background colour, not merely very dark terrain. Gamma inspection does not reveal block texture/detail inside them. Combined with the user's direct observation that collision/blocks exist there, this points at a **world-data-present but GPU-mesh-not-yet-published** gap rather than missing blocks, skylight failure, water/lava simulation, or a fragment-shader darkness bug.

The important discrepancy in the capture is therefore not the frame time. It is the producer frontier: world data can be ahead of the GPU mesh publication frontier, while the camera can see beyond the contiguous published radius.

## Why this is not fixed with fog or a frame-budget cap

R79 deliberately does **not**:

- pull fog inward to `PublishedRenderRadius`;
- reduce render distance;
- add a new frame-pacing fuse;
- increase worker count adaptively;
- treat absent neighbour chunks as AIR and publish provisional boundary faces;
- block the main thread waiting for meshes.

Those approaches either hide the symptom, change visual distance, risk frame-time regressions, or create temporary incorrect geometry.

## Structural fix

The source-style squared-distance priority remains the primary scheduling key. R79 changes only ties at the same squared distance:

1. The main thread caches the camera's horizontal view direction.
2. Chunks within the immediate 5x5 safety core keep top priority.
3. Beyond that core, equally-near candidates in the forward camera cone are selected before equally-near side/back candidates.
4. A farther chunk can **never** jump over a nearer chunk.
5. Mesh publication still requires the exact existing 3x3 chunk neighbourhood; no provisional topology is introduced.
6. Generation and mesh worker counts, work limits, fog distances and renderer hot paths remain unchanged.

This targets the actual failure mode: if the voxel data already exists for visible terrain, its GPU publication no longer waits behind arbitrary same-distance chunks behind the player.

## New proof telemetry

F3 now reports:

- `frontier data/mesh A/B` — why the first incomplete contiguous ring is incomplete;
- `viewGap data/mesh A/B` — missing chunks currently intersecting the camera frustum, split into:
  - `data`: no `ChunkColumn` exists yet;
  - `mesh`: `ChunkColumn` exists but no drawable GPU mesh is published.

The view scan is run only when the F3 HUD text refreshes, not in `VoxelWorld.Update()` or the draw path.

Interpretation for the next runtime capture:

- `viewGap data/mesh 0/N` confirms the user's report exactly: blocks/world data exist, GPU mesh publication is the gap.
- `N/0` means generation itself is behind the camera.
- both non-zero means both producer stages contribute.
- `0/0` while black holes are still visible would rule out publication lag and the next target would be section-mesh integrity / direct draw submission.

## Files changed from R78

Only production files:

- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`
- `Assets/BlockcraftPort/Rendering/MainPerformanceHud.cs`

Byte-locked to R78 by validator:

- `ChunkMesher.cs`
- `ChunkRender.cs`
- `MainChunkDirectRenderer.cs`
- `VoxelLighting.cs`
- `VoxelRenderEnvironment.cs`
- voxel shaders
- `ChunkGenerator.cs`
- `BiomeGenerator.cs`
- `MobAI.cs`
- `MobSpawner.cs`
- `MainPlayerController.cs`

Therefore R79 does not modify the expensive renderer/mesher, lighting algorithm, biome/world generation formulas, AI or player physics.

## Validation

`Tools/validate_r79_visible_mesh_publication.py`: **50/50 PASS**.

The validator checks, among other things:

- Unity target 2022.3.62f3;
- source fog distances untouched and no GPU-ready fog clamp;
- no R77 pacing fuse/adaptive worker boost;
- distance remains the primary generation and mesh key;
- view direction is only an equal-distance tie-break;
- exact 3x3 mesh-neighbour requirement retained;
- no provisional missing-neighbour mesh;
- F3 gap telemetry is outside the per-frame world update;
- R76 cross-chunk fluid seam seed retained;
- R78 fluid-light classification retained;
- hot renderer/generator/AI/player files byte-identical to R78;
- production diff limited to the two files above;
- C# delimiter balance;
- priority invariants: nearer always beats farther, forward wins only equal-distance ties.

## Runtime verification still required

There is no Unity Editor/Player runtime available in this environment, so this revision is not claimed as runtime-verified. Test the same location and camera direction in Unity and inspect the new F3 `viewGap data/mesh` values. Those counters are specifically added so the next result can be diagnosed from evidence rather than by masking the symptom.
