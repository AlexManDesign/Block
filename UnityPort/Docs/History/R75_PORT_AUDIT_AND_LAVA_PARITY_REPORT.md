# R75 — PORT AUDIT + SOURCE LAVA PARITY

Дата: 2026-09-20  
Target: Unity **2022.3.62f3**

## Эталонные входные файлы

В этой ревизии нормативной реализацией остаётся присланный WebGL2 source. Проверка привязана к точным SHA-256:

- `main.js`: `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`
- `meshWorker.js`: `48083309934008c53b804065bf2c1aea480b328b8d809084ec7f14188e645ebb`
- `genWorker.js`: `6b1d48b13e88239e7f1e1ccb1b9eec344a706d2ba392c14566f9806a99553a36`
- `tex.zip`: `a166d964d95f07900eb28547d6352bb2a05ae014bf0bb3da6d97744d9666a129`

`main.js` задаёт gameplay/runtime semantics, `genWorker.js` — генерацию, `meshWorker.js` — mesh/light/render semantics. Unity-реализация может использовать другой внутренний механизм, если observable result и алгоритмические ограничения остаются эквивалентными.

## Что сделано в R75

Главный новый перенос — полная runtime-семантика лавы из source вместо одного статического `Lava` блока.

### Block/runtime representation

Добавлены два append-only BlockId после существующего хвоста, поэтому старые числовые ID не сдвигаются:

- `Lava` — source, level 3;
- `LavaFlow2` — level 2;
- `LavaFlow1` — level 1.

Registry теперь централизованно знает lava family, level, next-level block, surface height и emission.

### Source queue algorithm

В `VoxelWorld` перенесены source-параметры `main.js`:

- отдельные normal/seed очереди;
- максимум **20 000** записей на очередь;
- cadence **500 ms**;
- за tick выбирается максимум **240 normal + 40 seed** кандидатов;
- максимум **60** mutation candidates;
- клетки дальше **96** блоков по X/Z от игрока возвращаются в seed queue;
- редактирование блока будит семь клеток lava neighbourhood;
- опубликованный generated chunk добавляет открытые lava cells в seed queue.

Работа выполняется батчем: изменения освещения/меша дедуплицируются по затронутым секциям, чтобы не превращать один lava tick в серию синхронных remesh на каждый блок.

### Exact flow/contact semantics

Перенесены source-правила:

- unsupported flowing lava постепенно теряет level и исчезает;
- вниз всегда течёт `LavaFlow2`;
- горизонтальное растекание возможно только при level > 1 и соответствующей опоре/source lava под клеткой;
- контакт с водой или water-carrier:
  - source lava → `Obsidian`;
  - flowing lava → `Cobblestone`;
- water mutation будит lava neighbourhood, lava mutation будит обе fluid families.

### Meshing/light parity with `meshWorker.js`

Использованы именно worker-значения, а не визуальное приближение:

| State | level | top surface | block light |
|---|---:|---:|---:|
| `Lava` | 3 | 0.875 | 14 |
| `LavaFlow2` | 2 | 0.55 | 13 |
| `LavaFlow1` | 1 | 0.30 | 12 |

Если над клеткой находится любой lava-family block, верхняя поверхность текущей лавы поднимается до полного блока, как в `meshWorker.js`. Лава остаётся в terrain/opaque render pass source-порта и не переезжает в water pass.

### Gameplay semantics

Flow lava теперь учитывается в:

- движении/состоянии `inLava` игрока;
- damage игроку и мобам;
- collision/spawn occupancy;
- mining restrictions;
- fishing bobber;
- world/ship collision;
- X-Ray classification.

Bucket повторяет важное различие source:

- пустое ведро **забирает только source** `Water`/`Lava`;
- ray проходит сквозь flowing water/lava;
- заполненное ведро может заменить fluid cell;
- те же правила применены к docked ship interaction.

### Performance diagnostics

F3 получил:

- `lavaQ`;
- last/max lava work time;
- lava ticks;
- lava spike attribution.

Это позволяет отделить fluid workload от generation/light/mesh/upload при реальном профилировании.

## Renderer audit

R75 намеренно **не переписывает** уже быстрый renderer. Существующая архитектура остаётся:

`generation -> cross-chunk lighting -> worker-style mesh build -> stale-version barrier -> bounded main-thread GPU upload -> direct chunk draw`

Ключевые свойства:

- нет GameObject/Renderer на каждый блок;
- секционные chunk meshes;
- отдельные render passes/buffers;
- persistent/direct render paths;
- ограниченные frame budgets и продолжимые очереди;
- приоритет ближайшей/видимой геометрии;
- entity batching вместо GameObject на каждого моба.

R75 меняет `ChunkMesher` только для трех lava states и не меняет `MainChunkDirectRenderer`, `ChunkRender`, streaming priority или GPU upload architecture. Поэтому hot path сохраняется. Новый реальный FPS в этой среде **не измерялся**: Unity Editor/Profiler отсутствует. Последний приложенный к R74 runtime capture (~85.8 FPS при CPU/GPU ~18.4/3.0 ms) относится к R74 и не выдаётся за результат R75.

## Аудит текущего переноса

Оценка ниже означает точность уже представленных в Unity систем, а не утверждение, что вся браузерная игра перенесена на 100%.

| Подсистема | Состояние | Проверка / замечание |
|---|---|---|
| Chunk/direct renderer + streaming | **Высокая** | R49 current 30/30; R75 не меняет direct renderer. Архитектура сохраняет быстрый chunk/batch path. |
| Water + lighting | **Высокая** | Current R32 31/31; R72-R74 перенесли edge seed, aquatic carriers, flow/support semantics и cooperative cross-chunk lighting. |
| Lava | **Высокая после R75** | R75 lava contract 83/83; queue/cadence/limits/contact, worker heights/emission и gameplay fluid-family semantics проверены. |
| Biomes / climate / terrain noise | **Высокая, унаследована** | В проекте 40 `BiomeId`; core biome/noise/lookup файлы не менялись. Исторический R39 oracle зафиксировал exact 15 000 columns / 3 seeds и 50 000 climate vectors. Oracle JSON не входит в текущий архив, поэтому численный прогон R39 сейчас не повторялся. |
| World structures | **Высокая/частично повторно проверена** | Реализованы desert well, pyramid, pillager outpost, ruined portal, deepslate mineshaft, shipwreck. R49 повторно подтверждает ship structure/render contract; полный structure oracle в R75 не перезапускался. |
| Mob AI / spawn / render | **Высокая** | Current R56 76/76: passive/hostile/fish/shark/slimes, lifecycle, RNG-sensitive death/feed/breed order, bow pass, direct entity batching. |
| Survival / items / TNT | **Высокая для представленного набора** | Current R51 71/71, включая hp/hunger/air/exhaustion, buckets, flint, TNT/explosions и ship-local variants. |
| Block lifecycle | **Высокая для уже представленных блоков** | Wheat, farmland/grass, six saplings/tree growth, fire, fast leaves перенесены. Carrot/potato и pumpkin/melon stems ещё отсутствуют. |
| Ships | **Продвинутый partial/high** | Ship structure/runtime, local block interactions, buoyancy/cargo-related runtime и direct moving buffers уже есть; оставшиеся source edge-cases требуют отдельной полной ревизии. |
| Keep Inventory | **Partial-high** | 36 inventory slots/respawn semantics есть; authoritative 4 armor slots + настоящий offhand ещё не завершены. |
| Mods | **Partial** | Gates для `torch/keepinv/minimap/fastleaves/xray/shipctl/sharks` есть; полный source Mods UI/ownership layer ещё не перенесён. |
| Minimap | **Не завершён** | Gate есть, renderer/cache/UI source minimap ещё отсутствуют. |
| Armor/offhand equipment | **Неполный** | Есть hooks/visual support, нет полного authoritative inventory/save/UI состояния. |
| Sound | **Неполный** | Требуется отдельный перенос event/sound layer из source. |
| Mobile/UI parity | **Partial** | Основные controls/UI присутствуют, но полный browser/mobile UI/ads/ownership слой не считается source-complete. |

## Biome/AI preservation check

R75 не вносит новые приближения в уже перенесённые биомы и AI. Зафиксированные hashes текущих core-файлов совпадают с ранее source-validated веткой:

- `BiomeGenerator.cs`: `2dce06eefe84ead4a8c6be0adecc98873c885d9c7cad938c562cf1e56359cc99`
- `MainTerrainNoise.cs`: `d8e2dd2b7786554f0c3b605db86242693e1d1c9300a6a48e0134644fc9c964c3`
- `MainBiomeLookup.cs`: `1b4d197543cb7d1b111b7009753c2098153d58c614117dfd320d7f610de5a669`
- `ChunkGenerator.cs`: `f149cf0a02cbf4d008af8bb814115ca50db125492a7203701f192180c742ac54`
- `MainChunkDirectRenderer.cs`: `8e796056a611b6cf28c8d8d961d0af0a1146f9c7a4c2fb8a17713ff02062bee6`

Mob-focused source contract повторно запущен на текущем `main.js`/`tex.zip`: **76/76 PASS**.

## Validation R75

Совместимые актуальные проверки:

- `validate_r75_lava_source_parity.py` — **83/83 PASS**
- `validate_r32_water_light.py` — **31/31 PASS**
- `validate_r49_current.py` — **30/30 PASS**
- `validate_r51_survival_items_tnt.py` — **71/71 PASS**
- `validate_r56_mobs.py` — **76/76 PASS**

Итого: **291/291 PASS** с пересечением проверяемых invariants.

Дополнительно R75 validator проверяет delimiter balance изменённых production C# файлов и отсутствие JavaScript в production tree.

### Что не было возможно проверить здесь

- Unity Editor compile;
- PlayMode;
- Unity Profiler / новый FPS / GC/frame-time capture;
- historical R35/R39 numerical oracle, потому что их JSON oracle files отсутствуют в текущем архиве.

Это не заменяется догадкой: эти пункты остаются явно непроверенными runtime/fixture checks.

## Следующая очередь переноса

Приоритет после R75:

1. carrot/potato + pumpkin/melon stem lifecycle, items/textures/drop semantics;
2. source minimap renderer/cache/update budget/UI;
3. authoritative armor + offhand inventory/save/equipment UI;
4. полноценный Mods UI / ownership state вместо standalone default adaptation;
5. sound/event layer;
6. оставшиеся блоки/items/recipes/functional edge-cases;
7. полный ship source edge-case audit;
8. финальный Unity Editor compile + PlayMode + Profiler regression по renderer/streaming/fluid/AI workload.

R75 специально продолжает перенос поверх уже быстрого renderer, а не заменяет его более тяжёлой Unity-архитектурой.
