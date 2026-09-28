# R61 — Block lifecycle parity, no ship work

Target: **Unity 2022.3.62f3**  
Canonical reference: submitted `main(20260919-202006).js` (SHA-256 `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`).

## Scope decision

The user explicitly said the ship is not needed. R61 therefore does **not** extend, refactor or tune the ship subsystem. The R60 ship runtime, moving renderer, shipwreck templates and ship shaders are byte-identical in R61. They remain in the inherited project only to avoid an unrelated destructive change/regression.

R61 instead continues ordinary-world gameplay parity: `main.js` crop/farmland/grass, sapling/tree and fire lifecycle.

## Implemented from main.js

### Crops / farmland / grass

Ported the behaviour represented by `y4()/tB()/nB()`, `ZP()/qP()/jP()` for the blocks currently represented by the Unity port.

- Wheat has 4 stages and progresses every **35 seconds** of accumulated growth time.
- Moist farmland contributes full elapsed time; dry farmland contributes **0.5x** elapsed time.
- The crop scheduler runs at the source **1 second** cadence.
- A crop is removed if the block below ceases to be farmland/moist farmland.
- Hoe use converts grass/dirt with free space above to dry farmland.
- Wheat seeds plant `Wheat0` only on farmland/moist farmland and consume one seed in survival.
- Farmland/grass maintenance runs at the source **2 second** cadence.
- Hydration checks water in the source **±4 X/Z, Y+0..1** volume, including represented waterlogged aquatic blocks.
- Dry farmland with no crop above reverts to dirt after **90 seconds**.
- Dirt adjacent to an edit is queued for grass conversion after **60..180 seconds**; the source grass-neighbour search volume (`dy -1..3`, `dx/dz -1..1`) is preserved.
- Farmland keys and crop elapsed timers are saved/restored in source coordinates.

Current representation limit: the canonical source also defines carrots, potatoes, pumpkin stems and melon stems. Those BlockIds/textures are not yet represented in this Unity port, so R61 implements exact lifecycle timing for **wheat** rather than inventing placeholder blocks.

### Six saplings / tree growth

Ported `zJ()/oh()/nh()/Ah()/ih()/wf()` for all six saplings already added in R60:

- oak
- birch
- spruce
- jungle
- acacia
- dark oak

Source timing is preserved:

- initial growth delay: **90..180 seconds**;
- scheduler cadence: **1 second**;
- failed attempt retry: **30 seconds**;
- saved elapsed sapling time is rounded like source `Math.round`; restored delay is randomized again to 90..180.

Tree geometry follows the canonical `Ah()` algorithms and distributions, including:

- dark oak 2x2 requirement, 5..6 trunk height and paired canopy centres;
- spruce 25% tall variant, 10..13 vs 6..8 heights and alternating cone radii;
- jungle 8..11 height and three canopy layers;
- acacia 5..6 height, random horizontal bend and 3/2-radius crown;
- oak 30% large form, 7..9 height and side canopy blob;
- normal oak and birch height/canopy rules;
- source-style 60% corner thinning for canopy layers with radius >= 2;
- tree replacement only into air/cross-plant spaces.

If a growth attempt fails, the sapling is restored and its retry timer remains authoritative even though Unity block-edit callbacks re-register the cell. This avoids a callback-specific divergence from the source's intended `r.t=0; r.delay=30` result.

### Fire lifecycle

Ported `XJ()/uh()/Eh()/sh()/Qf()`:

- maximum **300** tracked fire cells;
- initial / continuing update interval **0.7..1.3 s**;
- fire age capped at **15**;
- touching represented water extinguishes immediately;
- fueled fire at age >=15 extinguishes with **20%** chance per fire update;
- unsupported/no-fuel fire uses the source age>=4 / **35%** extinction rule;
- ignition chance is **0.9 upward / 0.7 other directions**;
- a burned flammable cell becomes fire **80%** of the time and air **20%**;
- exactly **3** random neighbouring spread trials per update, each with **35%** ignition chance when adjacent to fuel;
- saved fire age is restored with source **1.2..2.2 s** next-update delay.

The source flammability predicate includes every represented log/plank/leaf/fence/sapling/cross plant plus wool/bookshelf families. R61 exactly covers the corresponding BlockIds that exist in this Unity project. Wool/bookshelf blocks do not yet exist here, so they are not fabricated solely for the fire table.

## Performance / renderer safety

`MainBlockLifecycle` is deliberately separate from rendering. It writes through the existing `VoxelWorld.SetBlockDeferredPersistent()` path, so lighting/meshing/GPU upload remain staged and there is no second renderer or synchronous whole-world rebuild.

R61 does **not** modify these performance-critical / parity-locked systems:

- `MainChunkDirectRenderer.cs`
- `ChunkMesher.cs`
- `ChunkRender.cs`
- `VoxelLighting.cs`
- `ChunkGenerator.cs`
- `BiomeGenerator.cs`
- `MainTerrainNoise.cs`
- `MainBiomeLookup.cs`
- `MobAI.cs`
- `MobSpawner.cs`
- `MobProjectileSystem.cs`
- `MainEntityBatchRenderer.cs`
- `MainTransientRenderer.cs`

Each remains byte-identical to R60.

## Ship status

No ship work in R61. These files are byte-identical to R60:

- `Ships/MainShipRuntime.cs`
- `Rendering/MainMovingShipRenderer.cs`
- `Generation/MainShipwreckTemplates.cs`
- `Shaders/VoxelShipOpaque.shader`
- `Shaders/VoxelShipWater.shader`

## Production diff

Exactly five production files differ from R60:

1. `Core/MainWorldSave.cs`
2. `Core/PortBootstrap.cs`
3. `Gameplay/MainBlockLifecycle.cs` (new)
4. `Player/MainPlayerController.cs`
5. `Rendering/VoxelWorld.cs`

See `R61_PRODUCTION_PATCH.diff`, `R61_PRODUCTION_CHANGED_FILES.txt`, and `R61_CHANGED_FILE_HASHES.txt`.

## Validation

Current source/static checks:

- R61 lifecycle/source contract: **105/105 PASS**
- inherited R60 render/mod/gameplay contract: **80/80 PASS**
- R51 survival/items/TNT: **71/71 PASS**
- R55 dry-spawn parity: **58/58 PASS**
- R56 mob lifecycle/render parity: **76/76 PASS**

Selected total: **390/390 PASS**, with overlap between suites. The R61 suite also hash-locks renderer, worldgen, biome, mob and ship files and checks balanced delimiters in every R61-modified C# file.

Unity Editor / Roslyn is not installed in this execution environment, so these results are source/static regression checks rather than a claim of an actual Unity PlayMode build or measured FPS.

## Still not fully represented from main.js

The next ordinary-world gaps are mainly:

- carrots / potatoes / pumpkin / melon crop families and their items/textures;
- minimap implementation;
- remaining source UI/mod ownership behaviour;
- any still-unrepresented blocks/items that affect fire or crop tables;
- broader end-to-end PlayMode/performance verification in Unity itself.

Ship parity is intentionally excluded from future priority unless explicitly requested again.
