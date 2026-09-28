# R71 — Source-nearest cleanup

Purpose: remove dead R69/R64 camera/predictive-streaming compatibility code that remained after R70 restored source-nearest scheduling.

Production changes vs R70: exactly one file, `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`.

Removed dead symbols:
- `streamingViewX`, `streamingViewZ` (the two CS0414 warnings)
- `streamingCamera`
- `streamingPriorityCenter`
- `StreamingLeadX`, `StreamingLeadZ`, `PredictiveStreamingActive`
- no-op `UpdateStreamingViewDirection()`
- no-op `ViewTier()`
- no-op `ComputeStreamingPriorityCenter()`

The mesh priority now directly uses `center`; generation priority is initialized directly from `center`. This does not alter R70 scheduling semantics because all removed compatibility state had already been forced to the live player center and was never read to produce a different result.

Unchanged behavior:
- source-nearest generation ordering
- source-nearest mesh ordering
- generation pending window = 10
- maximum 3 new mesh submissions per scheduler pass
- fixed symmetric Unity worker pools
- exact main.js fog formula
- render-distance `[ ]` hotkeys
- Creative `F` flight toggle
- renderer, mesher, shaders, lighting, worldgen, biomes, AI, lifecycle/save code

Validation: `STATIC_VALIDATION_R71_SELECTED.txt` => 23/23 PASS.

Unity Editor is not installed in this environment, so this is a source/static regression pass rather than an Editor compilation run. The specific fields named in the reported CS0414 warnings no longer exist in R71.
