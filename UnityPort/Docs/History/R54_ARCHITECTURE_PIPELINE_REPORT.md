# R54 — ARCHITECTURE PIPELINE / CAVE STUTTER ROOT-CAUSE PASS

Target: **Unity 2022.3.62f3**  
Date: **2026-09-19**

R54 replaces the emergency anti-stutter throttles from R53 with explicit pipeline stages and resumable semantic work. The goal is to preserve the source algorithms and rendered result while preventing a single large cave/water/light operation from monopolizing one display frame.

## Scope

Production diff R53 -> R54 is intentionally limited to four files:

- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`
- `Assets/BlockcraftPort/Rendering/VoxelLighting.cs`
- `Assets/BlockcraftPort/Core/MainWorldSave.cs`
- `Assets/BlockcraftPort/Rendering/MainPerformanceHud.cs`

Generation algorithms, biome selection, `ChunkMesher`, block atlas/shaders, mob AI and ship runtime/renderer are not rewritten by this revision. The two ship production files remain byte-identical to the established R50/R51 implementation.

## 1. One streaming scheduler, not independent frame hacks

Generation integration, runtime cross-chunk lighting, deferred edit lighting, mesh-result integration/native upload, mesh scheduling and resource maintenance use one frame-headroom deadline. Work that is logically resumable preserves its own state and continues later. Atomic Unity/native operations use measured EWMA cost before starting rather than arbitrary byte or operation-count caps.

Removed R53-style/private limits include water millisecond slicing, mesh-upload byte caps and fixed per-frame counts for generated chunks, mesh results, mesh starts and uploads.

Worker-count limits remain because they are **CPU concurrency limits**, not quality/frame throttles. The pools deliberately leave logical cores available for Unity's main/render threads.

## 2. Versioned chunk pipeline

Runtime streaming is explicitly staged:

`generated column -> publish -> cross-chunk light stitch -> mesh snapshot/build -> stale-version check -> GPU upload -> render`

A newly published chunk enters a lighting barrier before it can be snapshotted for meshing. Cross-chunk `Yu()`-style propagation is a resumable `WorldStitchJob` whose seed cursor and sky/block queues persist across frames.

Mesh neighbourhood copying is performed on the persistent mesh worker, not on the frame thread. The job carries section version stamps; a result built from an older world/light version is discarded rather than briefly displayed.

Native `Mesh` upload remains on Unity's main thread because Unity mesh API calls are not worker-thread operations. CPU preparation is already complete before this stage.

## 3. Deferred cave relighting is a real continuation

Simulation-driven edits no longer call one potentially huge synchronous relight on the display frame. `DeferredRelightJob` persists the exact algorithmic state for:

- block-light removal;
- block-light re-propagation;
- direct-sky-column recomputation;
- skylight removal;
- radius-16 valid-light frontier seeding;
- skylight propagation;
- exact dirty-section collection.

Yielding changes only *when* the next node is processed. It does not cap propagation distance, lower quality, skip dark cave cells or replace lighting with an approximation. Mesh capture is blocked until the active light transaction finishes, so a half-updated light field is not uploaded.

## 4. Water is a source-cadence transaction

The source 200 ms water cadence, source candidate groups `400 + 80`, maximum 120 eligible changes and source flow-level rules are retained.

One logical water tick is computed into a speculative overlay. If the world changes while a cooperative tick is paused, the overlay is discarded and its candidates are retried against the current world instead of committing stale decisions. Queue side effects happen only after a successful commit.

Committed mutations are deduplicated for dirty mesh sections and enter the deferred lighting pipeline. The old R53 `0.65 ms` private water slice is gone.

## 5. Save path: snapshot -> persistent writer

Autosave no longer flattens/sorts the complete edit history and writes JSON synchronously during the 4-second gameplay cadence.

Voxel edits are kept in an append-only segmented journal. Main-thread autosave captures the journal head/count/version in O(1) together with the small gameplay snapshot. A persistent background writer then:

1. materializes/deduplicates source edits;
2. serializes JSON;
3. writes the temporary file and replaces the save;
4. builds a compacted journal;
5. asks the main thread to install compaction only if the journal version still matches.

Pause/quit can still force a synchronous flush so persistence semantics are not weakened.

## 6. Render-cache maintenance is event driven

The old periodic `every 45 frames -> scan every cached ChunkRender` pass has been removed.

Maintenance is now queued by state transitions:

- a completed mesh upload can immediately mark far CPU staging for deferred disposal;
- a normal one-chunk player transition checks only the old CPU/GPU retention threshold rings (`O(radius)`, not all cached renders);
- reducing render distance queues only the cache rings that crossed the new boundary;
- a rare teleport falls back to walking the bounded old cache square, never an unbounded periodic dictionary sweep.

The actual native disposal still runs through the shared maintenance scheduler.

## 7. F3 diagnostics

The F3 HUD exposes the architectural stages instead of a generic "stream" number, including runtime chunk-light stitching, deferred light work, water logical ticks/queue, mesh upload cost/bytes, save capture/background write and edit-journal size. This is diagnostic only; the profiler does not change simulation quality.

## Canonical source snapshots

- `main(20260917-174448).js`: `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`
- `meshWorker(20260917-174448).js`: `48083309934008c53b804065bf2c1aea480b328b8d809084ec7f14188e645ebb`
- `genWorker(20260917-174448).js`: `6b1d48b13e88239e7f1e1ccb1b9eec344a706d2ba392c14566f9806a99553a36`
- `tex(20260917-174449).zip`: `a166d964d95f07900eb28547d6352bb2a05ae014bf0bb3da6d97744d9666a129`

## Validation

Current compatible static/source-parity suites after the final R54 changes:

- R54 architecture: **63/63 PASS**
- R49 current renderer/game bridge: **30/30 PASS**
- R49 shipwreck/render: **41/41 PASS**
- R50 existing ship runtime regression: **59/59 PASS**
- R51 survival/items/TNT: **71/71 PASS**
- R52 static chest/furnace/crafting: **87/87 PASS**
- R52.1 compile-hotfix rules: **8/8 PASS**
- R52.2 scene setup: **9/9 PASS**

**Combined compatible matrix: 368/368 PASS.**

The project contains **55 C# + 12 shader production files** and no production JavaScript.

## Runtime verification boundary

This environment does not contain Unity Editor, `dotnet`, `csc` or `mcs`. Therefore R54 can be source-parity/static-structure validated here, but an actual Unity 2022.3.62f3 compilation, Profiler capture and measured cave `p95/p99` frame time must be performed in Unity. R54 intentionally keeps the F3 stage counters needed to identify any remaining runtime-specific spike without changing the game's visual result.
