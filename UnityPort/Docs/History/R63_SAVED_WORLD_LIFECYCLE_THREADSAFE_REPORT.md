# R63 — saved-world lifecycle main-thread hotfix

Target: Unity 2022.3.62f3
Revision: `R63_SAVED_WORLD_LIFECYCLE_THREADSAFE_2026-09-20`
Base: R62 flight streaming/fog

## Reproduced cause

The supplied stack trace is consistent with the R61 migration hook in `VoxelWorld.ApplyPersistentEdits()`:

`GenerationWorkerLoop -> ApplyPersistentEdits -> MainBlockLifecycle.NotifyBlockChanged -> OnBlockChanged -> ScheduleGrassCandidate -> UnityEngine.Random.value`

`ApplyPersistentEdits()` executes on the dedicated generation worker pool. `MainBlockLifecycle.OnBlockChanged()` was written as main-thread simulation code and contains both Unity RNG calls and mutable `Dictionary` state. With the R62 worker-count fix there can be 2–4 generation workers, so replacing only `UnityEngine.Random` would still leave the dictionaries/world queries incorrectly accessed from workers.

## Fix

Only `Assets/BlockcraftPort/Gameplay/MainBlockLifecycle.cs` changed.

* `Awake()` records the Unity main managed thread ID.
* `NotifyBlockChanged()` keeps ordinary main-thread gameplay edits immediate.
* Calls arriving from generation workers enqueue a managed `{x,y,z}` payload into `ConcurrentQueue<PendingBlockChange>` and do not call lifecycle simulation, world queries, Unity RNG, or mutate lifecycle dictionaries.
* `Update()` drains the queue on the main thread before fire/crop/sapling/ground simulation.
* Saved edits are applied to a generated `ChunkColumn` before that column is published into `World.chunks`; therefore a queued callback is processed only when `World.IsChunkLoadedAt(x,z)` is true. If it is still unpublished, it is requeued for a later frame.
* Queue drain is bounded to 4096 callbacks/frame during initial preload and 1024/frame after gameplay starts, preventing a very large edited save from causing one unbounded main-thread spike.
* Only entries present at the start of the drain are attempted, so unpublished callbacks cannot spin repeatedly in the same frame.

No gameplay timing/probability constants were changed. Sapling, crop, farmland, grass and fire algorithms remain the R61 implementation.

## Regression / scope

Production diff versus R62: exactly 1 file.

`Assets/BlockcraftPort/Gameplay/MainBlockLifecycle.cs`

Renderer, fog/streaming R62 code, chunk generation, meshing, lighting, biomes, mob AI and ship files are byte-identical to R62.

Selected static/source checks:

* R32 water/light: 31/31
* R51 survival/items/TNT: 71/71
* R55 spawn: 58/58
* R56 mobs: 76/76
* R60 render/mod/gameplay: 80/80
* R61 block lifecycle: 105/105
* R62 flight streaming/fog: 54/54
* R63 saved-world thread safety: 27/27

Total selected checks: 502/502 PASS (the suites overlap; this is not 502 unique behaviors).

## Runtime verification to perform in Unity

Open the same saved Survival world that produced the exception. During `preloading initial world before gameplay`, the Console must no longer contain `UnityException: get_value can only be called from the main thread` from `MainBlockLifecycle`. After load, verify existing crops/saplings/fire continue their lifecycle and perform a fast-flight pass to ensure the R62 streaming/fog behavior is unchanged.

Unity Editor is not available in this execution environment, so the actual PlayMode run must be confirmed in the local Unity 2022.3.62f3 editor/build.
