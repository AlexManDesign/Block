# R56 — MOB LIFECYCLE / RENDER PARITY

Target: Unity 2022.3.62f3  
Canonical gameplay/render reference: `main(20260917-174448).js`  
Scope: mobs only; preserve the R54/R55 world/render architecture and existing R47 AI rather than replacing it.

## Why this revision exists

R55 already had the source mob species, 20 Hz AI simulation, natural spawn cadence, collision/pathing,
aquatic movement, streaming persistence, damage/loot and direct entity batching. The next audit found several
source-visible pieces that had deliberately remained outside the earlier atlas/render scope. R56 closes those gaps
without converting mobs back to GameObject MeshRenderers and without changing world generation.

## Source mob set retained

- passive: pig, cow, sheep, chicken
- hostile: zombie, skeleton, creeper, spider, enderman
- aquatic: salmon, shark
- slime: big, medium, small

Natural spawning remains source-oriented: hostile check every 2 s, passive check every 20 s in daytime,
salmon check every 5 s, shark check every 12 s, plus one-time per-chunk fauna seeding (three 40% fish
opportunities and one 10% passive opportunity). The existing render-distance-dependent source cap formulas,
spawn rings, light tests, slime-chunk/swamp rules and aquatic depth tests were kept.

## R56 fixes

### 1. Skeleton bow in the direct entity renderer

`main.js` renders the skeleton skin first and then a separate bow quad from the item atlas. The old Unity entity
batch rendered the skeleton itself but omitted that second draw. R56 adds a third persistent direct batch using
`Voxel/main_items`, keeping the body in the entity atlas and player-held blocks in the block atlas. There is still
no per-mob MeshRenderer/MeshFilter.

The bow port preserves the source hand transform (`ld()`), walk-arm phase, baby scale, yaw axis coefficients,
0.34 half-size, -2.356 rotation, 0.9 shade and double-sided index winding.

### 2. Source mob particles

The source uses block-atlas fragments for several mob events:

- death: 8 BRICK fragments before slime splitting and loot RNG;
- feeding: 5 red-wool fragments;
- successful breeding: 8 red-wool fragments.

R56 adds a generic source-particle emitter that preserves the random-call order of `Yr()`. The exact 16x16
`bricks.png` and `red_wool.png` pixels from the supplied `tex.zip` occupy the two previously unused cells in the
existing 256x256 block atlas. No existing atlas pixels were changed.

Putting the death particle call before slime split/loot is not cosmetic only: it also restores the source shared-RNG
order for subsequent split/loot rolls.

### 3. Sheep breeding random order

The previous C# path chose the inherited parent colour before creating the child. In the source, child creation
(`fa()`) runs first (including the child's initial sheep-colour random), and only then is the parent colour chosen.
R56 now spawns/initializes the child first and performs the parent-colour random afterward.

### 4. F3 mob telemetry

F3 now displays total active mobs, hostile/passive counts and caps, fish, sharks, stored streamed mobs and the
current source spawn-far radius. This is diagnostic only; it does not alter spawning. It makes it possible to tell
apart "nothing spawned" from "spawned but not rendered" while testing caves/night/ocean.

## Architecture / performance constraints

- Existing R47 20-Hz mob simulation remains centralized; no new per-mob render component was introduced.
- Skeleton bows are appended to one persistent direct item-atlas mesh.
- Mob body, held-block and held-item batches keep adaptive UInt16/UInt32 index buffers and stable-frame rebuild skips.
- Particle geometry stays in the existing transient direct renderer.
- F3 mob counting executes only while the diagnostic HUD is being sampled.
- World generation, chunk mesher, shaders, R54 lighting/water/save pipeline and R55 spawn resolver were not rewritten.

## Production diff R55 -> R56

1. `Assets/BlockcraftPort/AI/MobAI.cs`
2. `Assets/BlockcraftPort/AI/MobSpawner.cs`
3. `Assets/BlockcraftPort/Rendering/AtlasLayout.cs`
4. `Assets/BlockcraftPort/Rendering/MainEntityBatchRenderer.cs`
5. `Assets/BlockcraftPort/Rendering/MainEntityModel.cs`
6. `Assets/BlockcraftPort/Rendering/MainPerformanceHud.cs`
7. `Assets/BlockcraftPort/Rendering/MainTransientRenderer.cs`
8. `Assets/Resources/Voxel/atlas.png`

## Validation

New R56 source/asset contract: **76/76 PASS**.

Mob regression/source suites:
- R29 source collision: **109/109 PASS**
- R30 movement exact: **132/132 PASS**
- R31 behavior exact: **73/73 PASS**
- R56 mob lifecycle/render: **76/76 PASS**
- mob-focused subtotal: **390/390 PASS**

Compatible architecture/game regression suites:
- R54 architecture: **63/63 PASS**
- R55 dry spawn: **58/58 PASS**
- R49 current renderer: **27/27 PASS**
- R51 survival/items/TNT: **71/71 PASS**
- R52 static functional blocks: **87/87 PASS**
- R52.1 compile hotfix contract: **8/8 PASS**
- R52.2 scene setup contract: **9/9 PASS**

Selected compatible total: **713/713 PASS**.

The R56 validator additionally checks the exact source hash, mob-type set/cadences, the bow constants and direct
batch architecture, exact `tex.zip` pixels of both new particle tiles, breeding/death call order, balanced C#
delimiters, the known CS0819 multi-`var` pattern, and absence of production JavaScript.

## Known remaining mob-adjacent work

This report does **not** claim all game UI/audio/item content is complete. In particular:

- a complete creative inventory / spawn-egg UI is still a separate gameplay/UI layer;
- the supplied texture bundle has no dedicated sprites for some logical mob drops (notably slime ball, egg and the
  wool item variants), so R56 does not silently substitute unrelated textures;
- a complete source audio pass for mob sounds remains separate from AI/render parity.

## Environment limitation

This environment has no Unity 2022.3.62f3 Editor/Roslyn compiler, so the project is statically/source validated but
Editor compile and PlayMode frame-time are not claimed as executed here.
