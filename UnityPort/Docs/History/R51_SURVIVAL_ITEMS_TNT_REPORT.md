# R51 — survival / item-use / ship TNT parity

Target: Unity 2022.3.62f3.
Canonical source: uploaded `main(20260917-174448).js` (SHA-256 `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`).
R51 is incremental over R50; the R49/R50 renderer, shipwreck, moving-ship mesh, buoyancy/flood/carve and mob-attachment layers remain in place.

## Added in R51

### Survival state and ticking
- source defaults: HP 20, hunger 20, air 10, exhaustion 0;
- exhaustion drains hunger at 4 points;
- source movement exhaustion rates (`.15/.015/.004`);
- fall damage threshold/formula (`>3.2`, `floor(distance-3)`);
- underwater/lava air drain and x4 air recovery;
- drowning, lava/head-lava, fire, magma, void, cactus and moving sweet-berry damage timers;
- natural regeneration at hunger >=18 every 4 s with +3 exhaustion;
- starvation every 4 s, never reducing HP below 1 by starvation alone;
- survival HP/hunger/air/dead persistence; exhaustion remains runtime-only like the source save subset.

### Source item-use
- all 25 source food values are parsed by the validator from `main.js` and matched to the C# catalog;
- 800 ms food-use cadence;
- golden apple can be eaten at full hunger and heals 8 HP;
- mushroom stew returns an empty bowl and drops it if inventory is full;
- empty bucket fluid ray uses 0.3 start / 0.1 step and converts selected stack with source `ST()` semantics;
- water/lava bucket placement and empty-bucket replacement;
- flint-and-steel durability initializes to 64, primes TNT, or places fire;
- generic string-key dropped-item path handles overflow items that are not represented by `MobItemId`.

### Moving-ship item-use / explosions
- empty bucket samples ship-local water/lava;
- water/lava bucket places fluid in ship-local voxel space;
- flint-and-steel places local fire or detaches local TNT into the global primed-TNT simulation;
- `SourceExplosion` now performs source `ta()`-style moving-ship lookup when the static world ray is AIR;
- ship explosion cells are grouped per vessel and committed with one geometry rebuild per affected vessel;
- local TNT chain reaction uses 0.10–0.35 s randomized fuse;
- bedrock and water immunity is retained.

## Validation

`Tools/validate_r51_survival_items_tnt.py <canonical main.js>`: **71/71 PASS**.

Combined compatible suites at packaging time:
- R32 water/light: 31/31
- R48 inventory/tools: 67/67
- R49 current renderer/shipwreck integration: 27/27
- R49 shipwreck/render: 41/41
- R50 ship runtime: 59/59
- R51 survival/items/TNT: 71/71
- total: **296/296 PASS**

The historical R47 validator contains an intentionally obsolete assertion that ship rendering must still be excluded; it is not part of the R51 combined suite. R50 hash-locks the already-audited AI production files instead.

## Environment limitation

This environment has no Unity Editor, `dotnet`, `csc`, or `mcs`, so this report does not claim an actual Unity compilation, PlayMode run, GPU frame capture, or FPS measurement. Static source-parity/contract tests and project structural checks are run here; final compile/runtime profiling still has to be executed in Unity 2022.3.62f3.
