# R52 — STATIC WORLD FUNCTIONAL BLOCKS

Target: **Unity 2022.3.62f3**  
Reference: uploaded `main(20260917-174448).js` (SHA-256 `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`).

## Scope

R52 continues the gameplay port for the **ordinary/static world only**. No new moving-ship gameplay was added in this revision.

**R52.1 compile hotfix:** `MainShipRuntime.cs` differs from R51/R52 only by splitting one illegal C# `var` multi-declaration into two legal declarations; gameplay is unchanged. `MainMovingShipRenderer.cs` remains byte-identical. `MainStructureGenerator.cs` only removes the unused local `half` variable and its assignments, clearing CS0219 without changing structure-generation logic.

Production delta from R51 is limited to seven files:

- new `Assets/BlockcraftPort/Core/MainWorldFunctionalBlocks.cs`
- `Assets/BlockcraftPort/Core/MainInventory.cs`
- `Assets/BlockcraftPort/Core/MainWorldSave.cs`
- `Assets/BlockcraftPort/Core/PortBootstrap.cs`
- `Assets/BlockcraftPort/Player/MainPlayerController.cs`
- `Assets/BlockcraftPort/Rendering/MainTransientRenderer.cs`
- `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`

## Chests

- 27-slot single chest storage.
- 54-slot double chest assembled from two same-facing neighboring chests.
- Source-style chest facing and left/right pairing metadata, with the source-to-Unity Z reflection applied only at the coordinate boundary.
- Breaking one half unpairs the remaining half.
- Stored stacks are dropped with their real counts when the chest is destroyed.
- Chest contents are persisted in world saves.

## Furnace

- Input / fuel / output slots.
- Source 10-second smelt duration.
- Source idle progress decay of `0.3 / sec`.
- `burn / burnMax` fuel state and Furnace/FurnaceLit block switching.
- Fuel slot accepts fuel only.
- Implemented source fuel timings for the currently represented item/block namespace, including stick/bowl, coal, lava bucket and wooden blocks.
- Lava-bucket fuel returns an empty bucket when consumed in the source-compatible case.
- Implemented smelting mappings for the currently represented ores/raw materials, sand, stone variants, cactus, clay, logs and foods.
- Furnace state persists across save/load.

## Crafting

- Survival inventory opens a 2x2 crafting grid.
- Crafting table opens 3x3.
- Recipe matching trims the occupied bounding box, supports shaped recipes, horizontal mirror and shapeless multiset matching.
- Craft consumption, stack merge/split and output durability initialization are implemented.
- Craft-grid leftovers and cursor items are returned to the inventory (or dropped if full) when the UI closes.
- A substantial set of source recipes is included for block/item IDs already present in the Unity port: planks/sticks, crafting table, furnace, torch, ladder, chest, food, buckets, rail/minecart/boat/fishing rod, tools, armor, bow/arrow, doors/trapdoors, common wood/stone shapes, dyes, TNT and other supported recipes.
- Per user request, R52 does **not** add the ship-wheel crafting path or any new moving-ship functional-block integration.

### Recipe coverage boundary

This report does **not** claim that every recipe in `main.js` is already craftable. Recipes whose ingredients/results depend on block/item IDs that have not yet been ported into the Unity registry remain for a later non-ship pass. The matcher and UI are in place; current recipe data is limited to the namespace the port can represent safely without renumbering existing BlockIds.

## Save / interaction integration

- Furnace and chest state are added to `MainWorldSaveData` and restored during bootstrap.
- Save coordinates are stored in source coordinate convention and reflected back at the boundary.
- Functional-block destruction notifications are attached only to `playerEdit` world mutations, specifically to avoid coupling the new R52 container logic to moving-ship/deferred-fluid paths.
- Static functional interaction runs before held-item use, matching the source interaction precedence for crafting table / furnace / chest.

## Validation

Current compatible validation suites after R52 changes:

- R32 water/light: **31/31 PASS**
- R49 current renderer/source contracts: **27/27 PASS**
- R49 shipwreck/render: **41/41 PASS**
- R50 existing ship runtime regression: **59/59 PASS**
- R51 survival/items/TNT: **71/71 PASS**
- R52 static functional blocks: **87/87 PASS**

R52.1 compile hotfix: **8/8 PASS**.

Total for the current fixed package: **324/324 PASS**.

The historical R48 validator contains two literal implementation-string assertions that are obsolete in R52: it expects direct `world.SetBlock(...)` for every block placement and assumes block pickups always use count `1`. R52 intentionally routes chest placement through its pairing function and now preserves real block-drop stack counts. Those two historical string checks therefore are not counted in the current total; the underlying inventory/pickup/place behavior is checked by the current R51/R52 suites.

Production static check: **67 C#/shader files** have balanced structural delimiters and there are **0 production JavaScript files**.

## Environment limitation

The execution environment used to build this revision does not contain Unity Editor, `dotnet`, `csc` or `mcs`. Therefore the results above are source/static parity and regression checks, not a claim that a Unity PlayMode run, platform build or measured FPS benchmark was executed here. The project target remains Unity **2022.3.62f3**.
