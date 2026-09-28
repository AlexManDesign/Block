# R81 — Visible Mesh Publication Priority

Target: **Unity 2022.3.62f3**  
Revision: `R81_VISIBLE_MESH_PRIORITY_2026-09-21`

## Symptom confirmed from the supplied R80 capture

The capture shows large, axis-aligned black holes while gameplay/collision data exists in those areas. F3 reports approximately:

- render distance target: `20`
- loaded chunk data: `611`
- GPU chunk meshes: `138`
- contiguous GPU-ready radius: `4`
- visible chunks: `67`

That combination is consistent with a **mesh-publication gap**, not missing terrain generation: CPU voxel columns are far ahead of drawable GPU chunk meshes. The black areas are also chunk-shaped and reveal already-generated underground geometry below/behind them.

The previous R79 rule used camera direction only as an equal-distance tie-break. With squared-distance still primary, GPU work could be spent on many nearer side/back chunks while farther chunks actually intersecting the camera view remained unpublished. R81 fixes that ordering mistake.

## Canonical source comparison

`main.js` schedules mesh candidates using squared horizontal chunk distance (`dx*dx + dz*dz`) and sorts nearest-first. R81 keeps that source distance metric, but adapts **Unity GPU publication order** so existing visible data is not allowed to wait behind off-screen meshes. Generation order itself remains unchanged.

This is a platform-specific presentation scheduling improvement, not a change to voxel/world algorithms.

## Production change

Only mesh-publication candidate selection changes:

1. **Tier 0 — immediate safety core:** the true 5x5 chunk area around the player (`Chebyshev radius <= 2`).
2. **Tier 1 — exact current camera frustum:** loaded chunks whose real non-air vertical bounds intersect the cached camera frustum.
3. **Tier 2 — forward preload cone:** chunks ahead of the camera that are not yet intersecting the exact frustum.
4. **Tier 3 — off-screen/back:** all remaining mesh work.
5. Inside each tier, source-style squared distance remains primary, with deterministic coordinate tie-breaking.

The frustum is copied on the Unity main thread. Candidate tests use loaded chunk data only. No Unity object is accessed by worker threads.

## What R81 deliberately does NOT do

R81 does **not**:

- reduce render distance;
- pull fog inward to hide holes;
- increase or cap the frame/streaming budget;
- create fake/cap geometry over missing chunks;
- increase mesh worker count;
- change `ChunkMesher`;
- change direct renderer or shaders;
- change generation/biomes;
- change lighting, water, lava, AI, mobs, player physics or save systems.

The generation producer/dequeue path is byte/structure-locked by validation against R80, so the fix cannot silently move the same problem into terrain generation.

## Temporary diagnostics

The visual-gap diagnostics are still needed for the next in-Editor verification, but remain explicitly marked for deletion:

`BC_TEMP_DEBUG ... REMOVE BEFORE RELEASE`

The F3 line now starts with `TEMP gaps` and is placed on its own row so `frontier data/mesh` and `viewGap data/mesh` are visible in screenshots. `TEMP_DEBUG_CLEANUP.md` remains the release cleanup registry.

## Validation

R81-specific validator: **36/36 PASS**.

Compatibility validators:

- R80 temp-debug marker policy: PASS; 22 markers remain explicitly tracked.
- R78 fluid relight root-fix: **34 applicable checks PASS**; the only old-script failure is its obsolete requirement that the revision string still say R78.
- R76 cross-chunk fluid seams: **24 applicable checks PASS**; the only old-script failure is its obsolete R76 revision-string check.
- R75 lava/source/meshWorker parity: **83/83 PASS**.
- R51 survival/items/TNT: **71/71 PASS**.
- R56 mobs/AI/render: **76/76 PASS**.

Total: **325 applicable assertions PASS**. The two reported failures in legacy scripts are revision-label checks only and are expected after advancing to R81.

The R81 validator additionally byte-locks the renderer/mesher/shaders, lighting, generation/biomes, AI/mobs and player controller against R80.

## Runtime verification to perform in Unity

Use the same world/location and keep render distance at 20. Do not lower quality settings to make the result look better.

Expected behavior after camera movement/loading:

- `TEMP gaps ... viewGap data/mesh` should favor visible mesh publication and trend toward `0/0` much sooner;
- black chunk-shaped holes should fill before unrelated off-screen/back chunks;
- `gpu-ready` may remain below the full render distance while streaming — this is acceptable as long as the current view remains coherent;
- FPS must be evaluated independently; R81 does not alter frame budgets or worker counts.

Unity Editor/Player execution is not available in this environment, so runtime FPS and visual closure are intentionally not claimed as verified here.
