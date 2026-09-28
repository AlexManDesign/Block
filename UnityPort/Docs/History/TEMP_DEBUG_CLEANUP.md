# Temporary debug cleanup registry

Canonical marker token: `BC_TEMP_DEBUG`.

Everything marked with this token is diagnostic/investigation code and must be reviewed for deletion before the release build. Do **not** remove it while the current visual-hole investigation is active unless the related bug is closed.

Current marked groups:

- `F3_PERFORMANCE_HUD` — entire F3 performance overlay component.
- `GPU_READY_RADIUS` — temporary contiguous GPU-ready radius telemetry.
- `GPU_READY_RADIUS_AND_FRONTIER_CLASSIFIER` — frontier classifier used by F3 only.
- `R79_VISUAL_GAP_TELEMETRY` — data-vs-mesh missing counters.
- `R79_VIEW_GAP_SCAN` — camera-frustum visual-gap scan.
- `F3_STREAM_PERF_TELEMETRY` — streaming/light/mesh public timing counters surfaced to F3.
- `F3_FLUID_TELEMETRY` — water/lava timing/queue counters surfaced to F3.
- `F3_MOB_SPAWN_TELEMETRY` — mob/spawn counters exposed only to F3.
- `F3_MOB_AI_PERF` — AI timing counters used by F3 spike attribution.
- `F3_ENTITY_BATCH_PERF` — entity-batch timing counters used by F3.
- `F3_SAVE_PERF` — save timing counters used by F3.

Release cleanup command:

```bash
grep -RIn "BC_TEMP_DEBUG" Assets/BlockcraftPort
```

Release rule: this grep must return **zero** matches after the final cleanup pass, unless a diagnostic feature is explicitly promoted to a permanent developer feature.

## R81 note

R81 does **not** add any unmarked diagnostics. The new on-screen line beginning `TEMP gaps` is inside the existing `BC_TEMP_DEBUG_BEGIN [F3_PERFORMANCE_HUD] ... REMOVE BEFORE RELEASE` block. The `R79_VISUAL_GAP_TELEMETRY` and `R79_VIEW_GAP_SCAN` groups remain temporary until the visual mesh-publication bug is closed.
