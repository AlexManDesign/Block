# R32 — WATER + EDIT LIGHT FIX

Date: 2026-09-12
Unity: 2022.3.62f3
Source main.js SHA-256: `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`

## Fixed

### 1. River water now reacts to block edits
The Unity port previously had no active source water-flow simulation. R32 ports the static-world part of main.js `h8()/ya()/rJ()/oP()/fE()/sP()` used by ordinary terrain water.

- Added `Flow3`, `Flow2`, `Flow1` without renumbering old BlockId values.
- Source levels: WATER=4, FLOW3=3, FLOW2=2, FLOW1=1.
- Source surface heights: 0.875 / 0.65 / 0.45 / 0.25.
- Every block edit queues the edited cell and six neighbours.
- Water simulation cadence: 200 ms.
- Candidate pull: 400 normal + 80 seed entries; mutation cap: 120 cells per tick.
- Two horizontal source-water neighbours promote a flow cell to source water.
- Downward spread creates FLOW3; horizontal spread decrements one level.
- Flow geometry renders with the source height ladder and water-water side faces only when the current surface is higher.

This fixes the reported case: deleting a block directly beside a river now queues the neighbouring source water, which fills/spreads into the opened cell on the source water tick.

### 2. Black/stale face after mining
The dark face in the screenshot was stale vertex skylight/AO, not intended directional shading. The old incremental path seeded only six immediate neighbours. If the valid skylight frontier was several cells around a wall/corner, the newly opened air cell could remain near zero until a later edit.

R32 keeps the incremental relight path but also seeds the already-valid radius-16 skylight shell before propagation. 15 is the maximum useful attenuation distance, so this restores the missing frontier without returning to a full synchronous 3x3 chunk rebake. Player edits still relight before their immediate mesh rebuild.

## Source limits intentionally unchanged
Moving-ship fluid carve/hold-cell checks are not added here, per request to leave ships alone. R32 targets ordinary world/river water.

## Validation

- R20: 65/65 PASS
- R21: 46/46 PASS
- R24: 44/44 PASS
- R26: 39/39 PASS
- R27: 31/31 PASS
- R28: 109/109 PASS
- R29: 109/109 PASS
- R30: 132/132 PASS
- R31: 73/73 PASS
- R32 water/light: 31/31 PASS

Unity Editor/Player is unavailable in this environment, so runtime PlayMode behaviour cannot be claimed as measured here.
