# R55 — dry spawn / persistent respawn parity

Target: Unity **2022.3.62f3**. Canonical behavior is the uploaded `main(20260917-174448).js` (SHA-256 `ab90b89b...86e3c`).

## What the source actually does

The new-world path does **not** simply place the player at `terrainHeight + 1`.

1. First-world metadata is `seed=56`, requested spawn `[-1063,-2850]`, yaw `PI/2`.
2. Before startup generation, the requested column is accepted only when source biome height is at/above sea level. Otherwise `Bl()` searches coarse land out to radius 3000, using radius step 1 below 200 and 12 afterwards, and requiring both `be(x,z).h >= sea` and density surface `b1(x,z) >= sea`.
3. The provisional loading pose is `[x+.5, max(height,sea)+2, z+.5]`.
4. After the complete startup generation square exists, `pg()` searches radius 0..48. Each candidate land column is scanned from Y=317 down to -63 with `VI()`.
5. `VI()` requires a source-solid support block, rejects source water, classic leaves, cross vegetation and the six classic logs, and requires two `JI()` headroom cells. It also rejects a candidate more than 6 blocks below the density surface so the player is not started deep in a cave.
6. A final wet-cell guard checks feet/head for water or aquatic blocks and re-scans the same column if needed.
7. Only then is `p.spawnPos` set. Lighting is built after this, and the startup mesh square is centered on the final `Q.pos`.
8. Normal world reload does not redo this dry search; it restores `pos`. `spawnPos` is persistent. A bed use sets it to block-center X/Z and Y+1. Death respawn uses exactly `spawnPos`.

## Unity changes

- Added `Player/MainSpawnResolver.cs`, a source-coordinate implementation of `JI/Jg/VI/Bl/pg`.
- `PortBootstrap` now uses source coarse anchor selection and source provisional `+2` Y.
- Added an explicit one-shot `VoxelWorld.InitialGenerationCompleted` barrier. New-world dry spawn is resolved there, **before startup lighting and meshing**. This avoids a post-ready teleport and keeps the first visible frame centered correctly.
- Startup generation targets remain centered on the provisional/coarse anchor; initial mesh targets are recentered on the final spawn, matching source stage order.
- Added persistent `spawn` to `MainWorldSaveData` with backward compatibility for old saves.
- Added `SetRespawnPoint`, source-space save/restore, and `RespawnAtSavedPoint` to `MainPlayerController`.
- Ordinary-world bed use now sets the respawn point. Sleep/time-skip UI is not claimed by this revision.
- `MainShipRuntime.cs` and `MainMovingShipRenderer.cs` are byte-identical to R54.

## Validation

- `validate_r55_spawn_parity.py`: **58/58 PASS**.
- R54 architecture retention: **63/63 PASS**.
- Current renderer: **30/30 PASS**.
- shipwreck/render regression: **41/41 PASS**.
- existing ship runtime regression: **59/59 PASS** (unchanged code).
- survival/items/TNT: **71/71 PASS**.
- static chest/furnace/crafting: **87/87 PASS**.
- compile-hotfix guard: **8/8 PASS**.
- editor scene setup: **9/9 PASS**.
- mob collision/movement/behavior: **109/109 + 132/132 + 73/73 PASS**.

Selected compatible suites total **740/740 PASS**. Historical validators that assert intentionally obsolete architecture (for example pre-R54 synchronous water scheduling or “ships excluded”) are not counted.

## Environment limitation

This environment has no Unity Editor, `dotnet`, `csc` or `mcs`. R55 therefore includes source-contract/static/regression validation, delimiter/CS0819 scans and archive integrity checks, but does **not** claim a real Unity compile, PlayMode run or profiler capture.
