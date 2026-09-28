# R60 — RENDER HELD-LIGHT + FAST LEAVES + KEEP-INVENTORY SOURCE PARITY

Дата: 2026-09-20  
Target: Unity 2022.3.62f3

## Эталон

Эта ревизия сверена с файлами, присланными вместе с задачей:

- `main(20260919-202006).js` — SHA-256 `ab90b89ba9c081fc442f3de02ec3afad431a44ce7a709a5c180c8a5c5ee86e3c`
- `genWorker(20260919-202007).js` — SHA-256 `6b1d48b13e88239e7f1e1ccb1b9eec344a706d2ba392c14566f9806a99553a36`
- `meshWorker(20260919-202005).js` — SHA-256 `48083309934008c53b804065bf2c1aea480b328b8d809084ec7f14188e645ebb`
- `tex(20260919-202007).zip` — SHA-256 `a166d964d95f07900eb28547d6352bb2a05ae014bf0bb3da6d97744d9666a129`

`main.js` остаётся нормативным источником gameplay/render semantics. Там, где Unity может сделать тот же результат дешевле без изменения поведения, R60 использует более подходящий Unity/C# путь.

## 1. Рендер: held torch light без дорогого Unity Light

В исходнике `rg()` факел в main-hand/offhand даёт block-light `14`, а значение передаётся в shader scalar `uHeldLight`; vertex shader добавляет радиальное затухание `(heldLight-distance)/15`.

В R59 shader-side формула уже существовала, но глобальный `_VoxelHeldLight` оставался равен нулю. R60 подключает реальное значение:

- `MainPlayerController.SourceHeldLightLevel` повторяет `rg()` для уже существующего `TORCH`;
- main-hand и offhand учитываются;
- `VoxelRenderEnvironment` обновляет один global float;
- одинаковое значение не отправляется Unity→native повторно каждый кадр;
- не создаётся `Light`, `GameObject`, дополнительный mesh или draw pass.

Это намеренная оптимизация относительно WebGL2: математический результат тот же для поддержанного факела, но ненужная установка одного и того же uniform в Unity пропускается.

**Ограничение:** source `GLOW/JACK_O_LANTERN/SEA_LANTERN` дают уровень `15`, но этих трёх source BlockId в текущем Unity enum ещё нет. Поэтому held-light сейчас exact для факела, но не полный для всех светящихся предметов source.

## 2. Fast Leaves / обычное опадание листьев

Добавлен отдельный `MainLeafDecay`, повторяющий `Eu()/hT()/gL()/dT()`:

- изменение блока планирует ровно 6 соседей;
- natural leaves получают таймер `(0.6 + random*2.4) * scale`;
- `scale=1` при Fast Leaves, иначе `2.5`;
- simulation cadence `0.25 s`;
- максимум `24` реальных распада за tick;
- overflow переносится на `0.3 s`;
- поддержка дерева ищется six-neighbour BFS до глубины `6`;
- порядок таймеров хранится через `LinkedList`, чтобы повторить insertion-order JS `Map`;
- проверяется только загруженный chunk;
- player-placed leaves помечаются meta bit `1` и не распадаются;
- распад использует deferred persistent edit, а не synchronous player-edit rebuild path.

### Drops листьев

`r2()` перенесён для представленных source leaves:

- Jungle sapling: `2.5%`;
- остальные saplings: `5%`;
- stick: следующие `2%`;
- apple: следующие `4%` только для source-классов LEAVES/DARK_LEAVES/AZALEA;
- один общий random sample используется для последовательности sapling → stick → apple;
- shears обходят `r2()` и выбрасывают сам leaf block.

Добавлены только в конец `BlockId`, без сдвига старых save IDs:

`OakSapling`, `BirchSapling`, `SpruceSapling`, `JungleSapling`, `AcaciaSapling`, `DarkOakSapling`.

Все шесть 16×16 текстур вставлены в свободные клетки существующего atlas **без ресемплинга**. R60 validator сравнивает RGBA-пиксели с `tex.zip` побайтно.

**Ограничение:** Unity `Random` сохраняет вероятности/диапазоны/порядок вызовов этой механики, но не обещает совпадение конкретной последовательности `Math.random()` между JS и Unity при одинаковом мире.

## 3. Keep Inventory / death drops

Source выполняет `W6()` при respawn, только если `keepinv` выключен. R60 переносит этот порядок:

- lethal damage запоминает точную feet-position смерти;
- при respawn и выключенном Keep Inventory 36-slot survival inventory сначала очищается через `ClearAndCapture()` и выбрасывается на `deathY + 0.6`;
- count и durability generic item-stack сохраняются;
- с включённым Keep Inventory содержимое 36 слотов не меняется.

**Ограничение:** source `W6()` также выбрасывает 4 armor slots и настоящий offhand stack. В Unity пока есть armor-point hook и visual offhand hook, но нет полного authoritative `p.armor[4]`/`p.offhand` inventory state. Поэтому эта часть Keep Inventory сейчас partial, а не 100% source-complete.

## 4. Mod gates

Добавлен единый `MainModSettings` для семи source ID:

`torch`, `keepinv`, `minimap`, `fastleaves`, `xray`, `shipctl`, `sharks`.

R60 уже подключает gates к torch held-light, keep-inventory, fast-leaves, X-Ray input и shark spawning. `shipctl`/`minimap` заведены в настройках, но полный source UI/ownership layer ещё не перенесён.

В браузерном source enable-state по умолчанию пустой и управляется Mods UI отдельно от ownership/IAP. В standalone Unity сейчас нет платёжного слоя и полноценного Mods UI, поэтому R60 сознательно делает gameplay mods доступными и включёнными по умолчанию через `PlayerPrefs`. Это **не source-exact UI default**, а практическая standalone-адаптация; API `SetEnabled()` уже есть для следующего UI-прохода.

## 5. Что R60 не переписывал ради скорости

Diff против присланного R59 ограничен 12 production/assets files. Следующие критические быстрые системы byte-for-byte совпадают с R59:

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

Следовательно R60 не меняет mesh topology, staged generation/light/mesh/GPU pipeline, biome math, mob AI или entity batching. Единственный render-runtime addition — cached scalar held-light update.

## 6. Проверка текущей ревизии

Новый `Tools/validate_r60_render_mod_gameplay.py` — **80/80 PASS**. Он проверяет:

- SHA канонического `main.js`;
- source constants/contracts этой ревизии;
- byte-identical R59 hot/inherited systems;
- held-light formula во всех world/entity/ship shader paths;
- leaf timers/BFS/cap/meta/drop thresholds;
- append-only BlockId;
- exact RGBA sapling atlas pixels;
- Keep Inventory death-drop semantics;
- lexical delimiter integrity изменённых C# files;
- отсутствие production JavaScript.

Дополнительно на текущих присланных source/tex повторно выполнены совместимые regression scripts:

- R32 water/light — **31/31 PASS**
- R51 survival/items/TNT — **71/71 PASS**
- R55 dry spawn — **58/58 PASS**
- R56 mobs — **76/76 PASS**
- R60 current contract — **80/80 PASS**

Итого выбранные текущие прогоны: **316/316 PASS**. Эти проверки частично пересекаются; число 316 не означает 316 независимых игровых функций.

Некоторые старые validators R21/R24/R47/R50/R52/R54/R58/R59 содержат исторические hash/string assumptions (“ships must be absent”, “file must be byte-identical to older revision”, старые comparison-tree paths) и ожидаемо дают ложные FAIL на более новой ветке. R60 не использует эти FAIL как признак gameplay-регрессии; соответствующие актуальные invariants проверяются новым contract и byte-diff против присланного R59.

## 7. Текущая точность по крупным слоям

| Слой | Состояние после R60 | Комментарий |
|---|---|---|
| Chunk/direct renderer + streaming | Высокая, унаследована | Critical hot path R59 byte-identical; held torch light теперь реально подключён |
| Fog/water/light | Высокая | R32 current 31/31; R60 не меняет core lighting |
| Biomes/worldgen/structures | Высокая, унаследована | R60 не меняет biome/noise/generator files; старые source oracle JSON отсутствуют в архиве, поэтому новый численный oracle здесь не перезапускался |
| Mob AI/spawn/render | Высокая | Current R56 76/76 на присланном main/tex; AI/spawner byte-identical к R59 |
| Ships | Продвинутый partial/high | Existing R50 runtime сохранён; R60 только расширил valid BlockId tail |
| Survival/items/TNT | Высокая для уже представленных IDs | Current R51 71/71 |
| Keep Inventory mod | Partial-high | 36 inventory slots exact order/drop; authoritative armor/offhand ещё нет |
| Hand Torch mod | High для TORCH | exact level 14 + shader falloff; source held level-15 blocks ещё не представлены |
| Fast Leaves mod | High | timings/BFS/cap/meta/drop distributions перенесены; RNG sequence engine-specific |
| X-Ray / Sharks | Existing implementation + gate | R60 добавляет source mod enable gate; сами системы уже были в R59 |
| Minimap mod | Не перенесён | Следующий отдельный слой |
| Crop/farmland/sapling/fire lifecycle | Неполный | `tB/qP/oh/Eh` всё ещё следующий крупный gameplay pass; R60 закрыл только `dT` leaves |
| Full armor/offhand inventory/UI | Неполный | Нужен для полного W6/keepinv и source equipment semantics |
| Full Mods UI / ownership | Не перенесён | Standalone default сейчас intentionally all-enabled |
| Sound layer | Неполный | Старый R59 audit уже отмечал это |

## 8. Что переносить следующим

Наиболее логичный следующий source-order pass — закончить block lifecycle, потому что R60 уже добавил необходимые sapling IDs:

1. `oh()/zJ()/nh()/Ah()` — sapling timers + рост oak/birch/spruce/jungle/acacia/dark-oak, включая dark-oak 2×2.
2. `tB()` — crop timers / farmland moisture growth-rate semantics.
3. `qP()` — grass spread + farmland wet/dry lifecycle.
4. `Eh()` — fire lifecycle.
5. Сохранение/восстановление `crops/growth/farmland/saplings/fires` в Unity save schema.
6. Затем source minimap (`385×385`, chunk-color cache, ~1.5 ms rebuild budget) и Mods UI.

## Ограничение среды

В текущей среде нет Unity Editor и Roslyn/C# compiler. Поэтому я **не заявляю** Editor compile, PlayMode capture, Windows/Android build или измеренный FPS. Сделаны static/source-contract, byte-diff и текущие regression checks. Реальную скорость нужно финально подтвердить Unity Profiler/Frame Debugger на целевой машине; при этом основной chunk/mesh/light/GPU hot path R60 не изменяет.
