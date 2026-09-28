# R69 — VIEW-FIRST TERRAIN STREAMING

Target: Unity 2022.3.62f3  
Base: R68_ADAPTIVE_STREAM_WORKERS  
Reference: supplied `main(20260919-202006).js`

## Runtime symptom from the user capture

The capture is not GPU-bound: about 187 FPS, p95 ~9 ms, p99 ~11.9 ms, CPU/GPU ~4.5/2.3 ms.
The streaming line is the important part: configured RD 6, contiguous GPU-ready radius 2, 203 CPU chunks,
141 GPU chunks, generation queue 10 and mesh backlog 288 sections. Mobs can therefore already simulate in a
generated CPU chunk while the corresponding terrain mesh is still waiting in the worker pipeline.

## What main.js actually does

This ordering is source-real, not an AI bug. `Bd()` renders a mob when its chunk exists in `rA.chunks` and
passes the frustum test; it does not require that the chunk already owns `chunk.mesh`. Terrain rendering is a
separate stage. `ol()` scans the configured `RENDER_R` square, requires the 3x3 chunk neighbourhood (`jL()`),
sorts mesh candidates by squared distance and submits a bounded number of mesh-worker jobs.

Therefore the correct fix is to shorten Unity's generated->mesh-visible gap, not to stop AI or hide mobs.
R69 intentionally leaves `MobSpawner`, `MobAI` and `MainEntityBatchRenderer` byte-identical to R68.

## R69 changes

### Camera-facing generation priority

The final generation membership is still exactly `GEN_R = RENDER_R + 1`, and the source WT=10 pending window
is unchanged. R69 changes only completion order:

1. the nearest 5x5 core (distance <= 2 chunks) always remains highest priority;
2. after that, chunks in roughly the forward 130-degree camera cone are fed first;
3. the remaining forward half-plane follows;
4. chunks behind the view are prepared last.

The original source squared-distance order is preserved inside each tier. The generation worker dequeue uses the
same view tier, so queued work from an earlier camera direction cannot continue blocking the current horizon.
The worker thread reads only two volatile floats; no Unity API is called from background threads.

### Camera-facing mesh priority

`TryFindBestMeshChunk()` still enforces these invariants first:

- real live `RENDER_R` work always beats predictive-flight work;
- 3x3 generated/lit neighbourhood must be complete;
- section version barriers and stale-result rejection are unchanged.

Inside the live render square, camera-facing chunks now win before side/back chunks, then the source nearest
squared-distance order applies. Mesh topology, AO, lighting, materials and GPU upload are unchanged.

### Adaptive spare worker now follows runnable pressure

R68 could keep assigning the elastic slot to generation whenever `gen=10`, even while both base mesh workers
were already busy and hundreds of mesh sections were runnable. R69 checks the actual active mesh count:
when mesh pressure is high and all base mesh workers are occupied, the spare Lowest-priority worker becomes a
mesh worker. If mesh is blocked, the spare slot falls back to generation to unlock 3x3 neighbourhoods.

Because the supplied machine is showing substantial frame headroom, the boost no longer shuts off on a harmless
14-15 ms outlier. It falls back immediately at an actual 18 ms slow frame (or sustained ~13.5 ms refresh EWMA),
with a 0.25 s cooldown. Base worker counts are unchanged.

### F3

F3 now shows `workers g/m current active g/m base g/m`, so it is possible to tell whether the elastic slot is
actually doing useful work. During the reported case the desired state is usually `1/3 ... BOOST` while mesh is
backlogged, then back to the base `1/2` after the visible horizon catches up.

## Explicitly unchanged

- source fog formula and GPU-ready independence;
- `ChunkMesher` and voxel topology;
- `ChunkRender` upload path;
- `MainChunkDirectRenderer` draw path;
- `VoxelLighting`;
- world generation / biome algorithms;
- mob AI, spawning and entity renderer;
- F-flight controls;
- predictive flight lead remains one chunk maximum.

No mob-render suppression was added. That would hide the symptom rather than fix terrain throughput and would
diverge from the source `Bd()` rule.

## Static validation

- R69 contract: 33/33 PASS.
- R32 water/light regression: 31/31 PASS.
- R67 frame-pacing/F-flight behavior checks remain intact; the old validator's single expected production-diff
  lock is intentionally obsolete because R69 changes `VoxelWorld.cs` and HUD.
- Production diff from R68 is exactly two files: `VoxelWorld.cs` and `MainPerformanceHud.cs`.

Unity Editor is not installed in this environment, so final PlayMode throughput must be confirmed from the user's
F3 capture rather than claimed from a local Editor run.
