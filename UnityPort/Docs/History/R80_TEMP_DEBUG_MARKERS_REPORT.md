# R80 — Temporary debug marker pass

This revision intentionally changes no game behavior.

All investigation-only diagnostics now use the canonical source marker `BC_TEMP_DEBUG` and are registered in `TEMP_DEBUG_CLEANUP.md`. The current F3 HUD stays enabled because it is still needed to diagnose the reported visual holes, but it is now explicitly marked as release-removal work.

The visual-gap telemetry introduced in R79 is separately tagged so it can be removed without touching the camera-aware streaming-order fix itself.
