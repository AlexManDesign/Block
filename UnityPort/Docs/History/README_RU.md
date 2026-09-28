# R75 — SOURCE LAVA PARITY + АУДИТ ПЕРЕНОСА (2026-09-20)

R75 продолжает перенос по присланным `main.js` / `meshWorker.js`, не переписывая уже быстрый direct chunk renderer. Добавлены source `LAVA_FLOW2/LAVA_FLOW1`, 500 ms normal/seed queue, лимиты 240+40 / 60 mutations / 20k queue / 96-block far deferral, source-lava→obsidian и flow-lava→cobblestone при контакте с водой. `meshWorker` semantics перенесены точно: поверхности `0.875 / 0.55 / 0.30`, emission `14 / 13 / 12`, lava остаётся в terrain/opaque pass. Bucket забирает только source WATER/LAVA, но ray проходит через flowing fluids; movement/damage/AI/fishing/ship collision теперь знают всю lava family.

F3 показывает `lavaQ` и lava work/max/ticks. Актуальная совместимая матрица: **291/291 PASS** (R75 83/83, R32 31/31, R49 30/30, R51 71/71, R56 76/76). Biome/worldgen и mob core не переписывались; подробный аудит точности и оставшийся backlog — `R75_PORT_AUDIT_AND_LAVA_PARITY_REPORT.md`. Unity Editor/Profiler в среде отсутствует, поэтому новый PlayMode/FPS не заявляется.

> Старые разделы README ниже оставлены как история ревизий.

## R72 water edge seed

Добавлен пропущенный source `EP(chunk)`: сгенерированная WATER/FLOW на границе с AIR теперь попадает в waterSeedQueue и обрабатывается обычным `fE()`-циклом.

# R69 — приоритет видимой геометрии

R69 исправляет ситуацию, когда CPU-чанк уже существует и мобы там симулируются, а terrain mesh ещё не дошёл до GPU. Генерация и mesh теперь сохраняют source nearest-first внутри приоритетных групп, но камера-направленные чанки готовятся раньше боковых/задних. Мобы не скрываются: исправляется именно terrain throughput. F3 показывает active worker count. Fog/ChunkMesher/AI/worldgen не менялись. Подробности: `R69_VIEW_FIRST_TERRAIN_STREAMING_REPORT.md`.

# R60 — HELD LIGHT / FAST LEAVES / KEEP INVENTORY (2026-09-20)

R60 продолжает перенос именно по присланному `main.js` и не переписывает уже быстрый R59 renderer. Подключён source held-torch light (`14`) через уже существующую shader formula и cached global float; добавлен exact-style leaf lifecycle `Eu/hT/gL/dT` (`0.25 s`, cap 24, BFS 6, player leaf meta bit 1, source drop thresholds) и шесть точных 16×16 sapling textures из `tex.zip`; Keep Inventory теперь управляет death-drop 36-slot survival inventory в source-порядке respawn. Добавлены единые gates семи mods; X-Ray и shark spawning также проходят через них.

R59 critical render/mesh/light/generator hot files и biome/AI core byte-identical. Новый source-contract **80/80 PASS**; выбранные current regressions R32/R51/R55/R56/R60 — **316/316 PASS** (с пересечением проверок). Editor/Roslyn в среде нет, поэтому PlayMode/FPS не заявляется. Полный аудит точности и список оставшихся расхождений: `R60_RENDER_MOD_GAMEPLAY_PARITY_REPORT.md`.

> Старые разделы README ниже оставлены как история ревизий.

# R59 — CURRENT-CENTER STREAM PRIORITY / SOURCE ORDER (2026-09-20)

R59 исправляет оставшееся расхождение быстрого полёта без изменения fog-distance и без снижения текущей скорости рендера. В R58 generation feeder уже вызывался каждый кадр, но pending jobs оставались FIFO от старого положения игрока. В `main.js` pending queue переупорядочивается `jw(qo())` относительно текущего chunk-center. Теперь generation worker выбирает ближайший ещё не начатый chunk относительно текущего центра; active jobs не отменяются, никакого directional/look-ahead bias нет. Дополнительно equal-distance порядок generation/mesh/render приведён к source X→Z order, чтобы `Dictionary`/порядок вставки не создавали асимметрию горизонта.

Fog остаётся R58/source-exact (`RD*16-26 → RD*16-2`, underwater `4→18`) и по-прежнему не зависит от GPU-ready radius. `ChunkMesher`, lighting, GPU upload, shaders, MobAI/MobSpawner и direct renderer побайтно не менялись. Актуальная совместимая regression/source-parity матрица: **794/794 PASS**. Подробности: `R59_STREAM_PRIORITY_SOURCE_PARITY_REPORT.md`.

> Старые разделы README ниже оставлены как история ревизий.

# R58 — SOURCE-EXACT STABLE FOG / PER-FRAME STREAM FEED (2026-09-19)

R58 исправляет fog-регрессию R57 по фактическому `main.js`. Воздушный туман больше **никогда не зависит от GPU-ready/published radius**: при фиксированном Render Distance он стабилен (`near = RD*16-26`, `far = RD*16-2`), а под водой остаётся `4 → 18`. Поэтому при ходьбе или быстром полёте fog не может внезапно прыгнуть ближе.

Причина недогрузки исправлена отдельно: chunk-generation feeder теперь вызывается каждый кадр в source-порядке `chunks → light → mesh`, использует общий frame deadline и bounded persistent worker queue; тяжёлая генерация на main thread не возвращалась. `PublishedRenderRadius` оставлен только диагностикой F3 (`rd target/gpu-ready`) и не влияет на картинку. Production diff R57→R58: только `VoxelWorld.cs`, `VoxelRenderEnvironment.cs`, `MainPerformanceHud.cs`. Актуальная совместимая regression/source-parity матрица: **666/666 PASS**. Подробности: `R58_FOG_STREAMING_SOURCE_PARITY_REPORT.md`. Unity Editor/Roslyn в этой среде отсутствует, поэтому PlayMode/FPS не заявляется как выполненный.

> Старые разделы README ниже оставлены как история ревизий.

# R56 — МОБЫ: LIFECYCLE / RENDER PARITY (2026-09-19)

R56 продолжает мобов поверх R55, не переписывая уже source-exact AI. Добавлен отсутствовавший отдельный item-atlas bow pass скелета внутри persistent direct entity batch; восстановлены source `Yr()` частицы смерти (BRICK, 8), кормления (red wool, 5) и размножения (red wool, 8) с исходным порядком RNG; исправлен порядок RNG цвета детёныша овцы; F3 показывает active hostile/passive/fish/shark/stored и source spawn caps/radius. Два particle tile взяты **точно** из supplied `tex.zip` и помещены в два ранее пустых atlas-cell без изменения остальных пикселей. Production diff R55→R56 — 8 файлов. Mob-focused проверки: **390/390 PASS**; выбранная совместимая общая матрица: **713/713 PASS**. Подробности: `R56_MOB_LIFECYCLE_RENDER_PARITY_REPORT.md`. Unity Editor/Roslyn в этой среде отсутствует, поэтому Editor compile/PlayMode не заявляется.

> Старые разделы README ниже оставлены как история ревизий.

# R55 — SOURCE-EXACT DRY PLAYER SPAWN / PERSISTENT RESPAWN (2026-09-19)

R55 исправляет startup spawn по фактическому `main.js`, а не по приближённой высоте биома. Новый мир сначала выбирает coarse generation anchor как source `Bl()`, генерирует startup-square, затем **до lighting/meshing** выполняет точный `pg()/VI()/JI()` поиск безопасной сухой опоры: радиус 48, top-down 317..-63, запрет воды/водных растений, classic leaves/cross-plants/six classic logs, два допустимых блока над ногами и source-проверка `densitySurface-6`, чтобы не появляться глубоко в пещере. После этого initial mesh-square центрируется уже на финальной позиции игрока. Provisional Y исправлен с `+1` на source `+2`.

`p.spawnPos` теперь сохраняется отдельно от текущей позиции: старые save без поля spawn используют текущую позицию, как source; клик по обычной кровати устанавливает respawn point. Существующий save загружается по сохранённой позиции и **не** пересчитывается на dry spawn — dry search применяется только при создании нового мира, как в `main`. Корабельные файлы этой ревизией не изменены. R55 source-contract: **58/58 PASS**; R54 architecture: **63/63 PASS**; выбранная совместимая regression/source-parity матрица: **740/740 PASS**. Unity Editor/Roslyn в этой среде отсутствует, поэтому реальный Editor compile/PlayMode не заявляется. Подробности: `R55_DRY_SPAWN_PARITY_REPORT.md`.

> Старые разделы README ниже оставлены как история ревизий.

# R54 — ARCHITECTURE PIPELINE / CAVE MICROSTUTTER ROOT-CAUSE (2026-09-19)

R54 заменяет диагностические ограничители R53 на staged/versioned pipeline: generation → cross-chunk lighting → worker mesh build → stale-version barrier → main-thread GPU upload. Water tick и cave relight являются продолжимыми транзакциями без урезания алгоритма; autosave использует O(1) snapshot append-only edit journal и постоянный background writer; периодический полный scan render-cache удалён в пользу event-driven maintenance. Production diff R53→R54 — только `VoxelWorld.cs`, `VoxelLighting.cs`, `MainWorldSave.cs`, `MainPerformanceHud.cs`. Совместимая матрица: **368/368 PASS**. Детали: `R54_ARCHITECTURE_PIPELINE_REPORT.md`. Unity Editor в этой среде отсутствует, поэтому реальный compile/Profiler/FPS не заявляется.

> Старые разделы README ниже оставлены как история ревизий.

# R47 — MOB AI DEATH / BURN / LOOT RNG PARITY (2026-09-17)

- Zombie/Skeleton под прямым дневным светом теперь повторяют source `bp()`: burn tick выполняется, а chase/wander/growl/land movement на этом source tick пропускаются. Skylight probe использует полную `Def.Height`.
- Slime split при смерти выполняется **до** loot rolls, как source `Kp()`.
- Death drops сначала полностью роллятся, затем создаются item entities; launch-angle random больше не вмешивается между loot rolls. Death Y = `mob.y + 0.5`.
- Исправлены source-exact count/roll semantics pig/cow/chicken/zombie/skeleton/spider/enderman/shark/slime-small; сохранён spider quirk `count || 1`.
- Enderman teleport возвращает 8 obsidian particles в старой и новой точке.
- Independent oracle: **15000/15000 exact PASS**; старый R46 loot ordering расходился в **6173/15000** случаев.
- Production diff R46→R47: только `Assets/BlockcraftPort/AI/MobAI.cs`; остальные 108 production-файлов byte-identical.
- R47: 52/52 PASS; сводка R20–R47: **1549/1549 PASS**; production JS: 0; корабли не реализуются.

> Старые разделы README ниже оставлены как история ревизий. Текущее состояние задаётся R47 и последующими revision reports.

# R46 — render idle cache / selection raycast / first-person UInt16 (2026-09-17)

- Selection outline больше не делает voxel raycast каждый неизменный кадр: строгий кэш по aim origin/direction/reach + отдельный `WorldQueryRevision`.
- `WorldQueryRevision` меняется на chunk integrate/unload и block/meta edit, но не на light/mesh-only работу.
- First-person meshes переведены на безопасный `UInt16` (фиксированный максимум 4096 вершин), индексы пакуются в переиспользуемый `ushort[]`.
- Held light/MVP material properties не переписываются, если их точное значение не изменилось; scratch-списки held visual переиспользуются.
- Cloud mesh использует `UInt16`; абсолютный максимум его сетки 23,716 вершин.
- Shaders, water/glass ordering, worldgen, AI, save и gameplay rules не менялись. Корабли не реализуются.
- R46: 49/49 PASS; сводка R20–R46: 1497/1497 PASS; production JS: 0.

# R45 — DYNAMIC INDEX BANDWIDTH + IDLE ITEM SKIP

R45 продолжает R44 без изменения картинки: адаптивный `UInt16/UInt32` index-buffer из R43 теперь применяется и к direct dynamic meshes — entity/held batch, particles, block/item drops, arrows, mining crack, falling/TNT, boat/minecart, fishing, pearls и red-box overlay. Пока объявленная vertex-capacity <= 65 535, индексы грузятся как `UInt16`; при росте выше лимита mesh автоматически переходит на `UInt32`. Память для упаковки берётся из `ArrayPool<ushort>`, поэтому новых GC-аллокаций на rebuild нет.

Дополнительно исправлен лишний idle hot path: `BuildItemMesh()` больше не запускается каждый кадр при полном отсутствии dropped items и mob arrows — пустой batch очищается один раз и затем пропускается до появления новой геометрии. Production diff R44→R45: только `MainEntityBatchRenderer.cs`, `MainTransientRenderer.cs`, `MainSourceObjectRenderer.cs`. Shaders/worldgen/AI/save/gameplay не менялись, корабли не добавлялись. Проверки R20–R45: **1448/1448 PASS**, новый R45 слой **57/57 PASS**, `NO_JS PASS`. Подробности: `R45_DYNAMIC_INDEX_BANDWIDTH_REPORT.md`. Unity Editor в этой среде отсутствует, поэтому runtime FPS/Play Mode compile не заявляются.

> Старые разделы README ниже оставлены как история ревизий. Текущее состояние задаётся R45 и последующими revision reports.

# R44 — SAFE RENDER PASS SPLIT

R44 продолжает R43 без изменения геометрии/шейдеров: большие static chunk command buffers отделены от маленьких dynamic overlay buffers. Теперь particles/drops/entities/vehicles/fishing/crack/selection меняют только overlay revision и не заставляют заново записывать сотни `DrawMesh` команд видимых чанков. Source-порядок сохранён через `BeforeForwardOpaque -> AfterForwardOpaque -> BeforeForwardAlpha -> AfterForwardAlpha`: celestial+terrain, затем dynamic objects/entities, затем world water+glass, затем clouds+selection. X-Ray и underwater sky visibility по-прежнему инвалидируют обе необходимые части.

Production diff R43→R44: только `MainChunkDirectRenderer.cs`, `MainPerformanceHud.cs`, `MainSkyRenderer.cs`, `VoxelWorld.cs`; Generation/AI/Core/Player/Resources/Shaders байт-в-байт не изменены. Проверки R20–R44: **1391/1391 PASS**, `NO_JS PASS`. Подробности выпуска находятся в `R44_RENDER_PASS_SPLIT_REPORT.md` рядом с архивом. Unity Editor в этой среде отсутствует, поэтому runtime FPS/Play Mode compile не заявляются.

> Старые разделы README ниже оставлены как история ревизий. Текущее состояние задаётся R44 и последующими revision reports.

# R39 — BIOME PRECISION C#

R39 продолжает R38 и уточняет численную точность biome/worldgen без JavaScript в Unity-проекте. Source-critical путь `iA()/pQ()/T1()/S1()` теперь принимает решения в `double`, как JavaScript `Number`, при этом runtime/debug поля остаются компактными, а существующие R35 кэши и pooled density lattice сохранены.

На старом float-пути source-oracle нашёл редкие отклонения surface height на 1 блок. После R39 проверка 15 000 случайных колонок по трём seed даёт exact parity по height, biome ID, temperature/humidity bucket, factor/mAmp и случайному `T1()` density sample. Packed 7D lookup отдельно проверен на 50 000 random climate vectors без расхождений. Также сохранена редкая source-semantics для invalid/NaN height через отдельный флаг, без попытки хранить NaN в integer height.

Production diff R38→R39: только `BiomeId.cs`, `BiomeGenerator.cs`, `MainBiomeLookup.cs`, `MainTerrainNoise.cs`. Render/AI/shaders/gameplay/atlas/BlockId не менялись. Проверки R20–R39: **1107/1107 PASS**, `NO_JS PASS`. Подробности: `R39_BIOME_PRECISION_REPORT.md`. Unity Editor в этой среде отсутствует, поэтому реальный Editor compile/play-mode/FPS test не заявляется.

> Старые разделы README ниже оставлены как история ранних ревизий и могут описывать уже устаревшее состояние. Текущее состояние задают верхние R39/R38 секции и соответствующие revision reports.

# R38 — C# COMPILE FIX + BIOME ECOLOGY / ORE RNG

R38 продолжает R37 без JavaScript в Unity-проекте. Исправлен `CS0136` в `MainSourceObjectRenderer.UpdateMinecart`: локальная скорость curve-ветки переименована в `curveSpeed`, физика не менялась. Также исправлен source-exact RNG руд: seed теперь берётся из raw 32-bit hash с исходными смещениями `.31/.77`, без потери младших битов через `float`. Проверка: 4000/4000 source-seed векторов exact.

Biome-dependent spawn повторно сверён с реальными `main` функциями: pig/cow/sheep/chicken не получают выдуманных biome whitelist, slime использует slime-chunk либо Swamp/Mangrove Swamp, salmon — подходящую воду до 24 блоков ниже sea level, shark — пять ocean biome ID. Оптимизация R38 выполняет biome/slime-chunk lookup только для slime.

Production diff R37→R38: только `MainSourceObjectRenderer.cs`, `MainTerrainNoise.cs`, `ChunkGenerator.cs`, `MobSpawner.cs`. Проверки R20–R38: **1016/1016 PASS**, `NO_JS PASS`. Подробности: `R38_BIOME_ECOLOGY_ORE_RNG_REPORT.md`. Unity Editor в этой среде отсутствует, поэтому реальный Editor compile/play-mode не заявляется.

# R33 — SOURCE OBJECT RENDER + FALLING

R33 продолжает перенос текущего `main(20260912-214015).js` (SHA-256 `ab90b89b…86e3c`) поверх R32. Главный render order теперь расширен до source-порядка `terrain -> W7 -> kB -> h7 -> pP -> tI opaque slot -> nT -> yd -> Bd -> rP -> $B -> water -> tI water slot -> glass`. Break particles и drops разделены на отдельные persistent direct meshes, чтобы между ними можно было вставить source-проходы без GameObject/MeshRenderer на объект.

Добавлен `MainSourceObjectRenderer`: falling sand/red sand/gravel с `rE()/OB()`-логикой (`g=28`, `ry`, low-to-high settling), boat/minecart `pP()` geometry backend, fishing bobber/line `nT()`, Ender Pearl `yd()` render/physics constants и red-box pass. Исправлена texture parity: bobber берётся из `tex`, Ender Pearl использует точный procedural fallback `TA(...,9057)`, iron/TNT/service tiles сохранены отдельно для source transient passes.

Проверки текущего слоя: R19 **24/24**, R20 **65/65**, R21 **46/46**, R24 **44/44**, R26 **39/39**, R27 **31/31**, R28 **109/109**, R29 **109/109**, R30 **132/132**, R31 **73/73**, R32 **31/31**, R33 **29/29** PASS. Подробности: `R33_SOURCE_OBJECT_RENDER_REPORT.md`.

**Не считать завершённым:** `tI()` moving ships пока только имеет правильные opaque/water slots; TNT gameplay, full boat/minecart riding, fishing inventory/reel loot, full Ender Pearl special-shape collision и общий inventory/crafting/UI port ещё продолжаются. Старые разделы README ниже оставлены как история ревизий и местами описывают состояние ранних R-сборок, а не R33.

# R30 — MOB MOVEMENT SOURCE EXACT

R30 доводит движение/AI мобов по `main.js`: общий 20-Hz `ad()/Hp()/bp()` clock, `No()/Ki()/Up()` path logic, `gt()/An()/W1()` collision, `Dp()` land physics и `pf()/yp()/Yp()/Mp()` aquatic movement. Последний `Ki()` fix сохраняет source-порядок: `pathT` уменьшается и A* при необходимости перестраивается до проверки стоп-радиуса `No()`. Для статического мира формулы и state order сверены с текущим source; moving-ship ветки `Fs()/CE()/C7()` не имитируются до порта ship runtime.

Проверки: R20 65/65, R21 46/46, R24 44/44, R26 39/39, R27 31/31, R28 109/109, R29 109/109, R30 132/132 PASS. Подробности: `R30_MOB_MOVEMENT_SOURCE_EXACT_REPORT.md`.

# R13 — Direct Chunk Renderer

R13 убирает GameObject/MeshRenderer/MeshFilter на каждый чанк. Мир рисуется централизованно через два переиспользуемых Camera CommandBuffer с source-like frustum culling и глобальным X-Ray wire -> depth clear -> classified solid. Быстрый player-edit R10, persistent workers R12 и клавиши дальности R11 сохранены.

# Blockcraft → Unity 2022.3.62f3

Эта ревизия сверена с файлами от **2026-09-09**: `main.js`, `meshWorker.js`, `genWorker.js` и `tex`. Приоритет — быстрый source-style voxel renderer, затем генерация/биомы и сущности/AI. В demo теперь включён `MobSpawner`.

## Что перенесено

### World renderer
- Чанк 16×384×16; worker-style generation/meshing; один logical chunk renderer с тремя submesh streams.
- Source `o / w / t`: terrain/cutout, water, transparent.
- Interleaved vertex 32 B: `position3 + uv2 + shade + sky + block`; chunk и entity batches используют один и тот же source payload.
- Source face culling, face shades, corner AO, AO diagonal, packed skylight/blocklight, 16×16 lightmap LUT.
- Atlas Point/no-mipmap/uncompressed, source UV inset, `Cull Off`, `ZTest Less`.
- Source water/lava fluid shapes, aquatic shell, cross/tall plants, bamboo, lily pad, sea pickle, carpet.
- Normal/underwater fog, held-light hook, day/night, source-like clouds and resource-pack sun/moon.
- Background generation/meshing + cached sections + budgeted Mesh API uploads for WebGL-like throughput without GameObject-per-block.
- Chunk upload теперь делает один contiguous index-buffer upload и три `SubMeshDescriptor` диапазона вместо трёх `SetTriangles` копий.

### World generation / biomes
- 40 generator biomes, including ocean variants, beaches, desert/savanna/plains, forests, taiga/snow, mountains/peaks, rivers, badlands, swamps, mushroom fields, bamboo/cherry/mangrove.
- Current `genWorker` multinoise/height/deepslate branch, caves/canyons, aquifers, ores, trees and biome vegetation.
- 111 C# block IDs are registered for the currently ported world/generator path.

### Entities / AI — added in this revision
- Source textures and cuboid models: pig, cow, sheep, chicken, zombie, skeleton, creeper, spider, enderman, salmon, shark and three slime sizes.
- Entity renderer теперь один динамический atlas batch: source 32 B vertex payload, alpha-test 0.5, те же light LUT/fog/held-light правила и source face shading.
- Source-style spawn buckets/cadence/caps for hostile/passive/aquatic mobs and sharks.
- Salmon groups, ocean-only sharks, slime chunks/cave spawn.
- Local cave-safe locomotion and source-inspired A*: 320 nodes, radius 22, step-down 3, repath 0.6 s.
- Render-поза мобов интерполируется между 0.05 s simulation ticks, как `Ka`/`ox..oz` в `main.js`, поэтому высокий FPS больше не показывает 20-Hz ступеньки.
- `MobSpawner` is enabled in `PortDemo` bootstrap.

### Player/demo
- WASD / mouse, Ctrl or double-W sprint, Space jump, double-Space creative flight, Shift sneak/down.
- Source-style speeds/step/gravity/ice behavior and sprint FOV.
- LMB break, RMB place, 1–9 block selection.
- Black block selection outline.

## Точность переноса

Подробная сверка находится в **`PORT_VALIDATION_2026-09-09.md`**. Важный момент: world/chunk renderer перенесён по исходным константам и draw split, но без Unity runtime в этой среде нельзя честно подтвердить screenshot-level pixel identity. White-sheep fur и spider/enderman eye overlays теперь композитятся из тех же source PNG. Полный renderer всё ещё не 100%: не подключены все source special-block geometries (`slab/stairs/fence/gate/wall/pane/door/trapdoor/ladder/torch/rail/shipwheel/pot`), X-Ray multi-pass и hand/item/armor/particle passes.

## Что ещё переносить

Следующие крупные слои: survival combat/damage/drops, skeleton projectiles, creeper explosion, exact enderman/spider behaviors, breeding/babies/shearing/eggs/slime split; затем inventory/hotbar/crafting/furnace/chests, save/world UI, structures/loot, mods (torch/keep inventory/minimap/xray/ships/sharks controls), audio/mobile UI и оставшиеся item/entity render passes.

## Запуск

1. Распаковать архив.
2. Unity Hub → открыть папку через **Unity 2022.3.62f3**.
3. Дождаться импорта. `PortAutoSetup` настраивает pixel textures и при необходимости создаёт `Assets/Scenes/PortDemo.unity`.
4. Открыть `PortDemo` и Play.

В текущем окружении Unity Editor/C# compiler отсутствует, поэтому здесь выполнена статическая, а не runtime-компиляционная проверка.

## Render R8 — производительность

В R8 streaming/meshing hot-path приблизен к `main.js`: ограниченный worker pool 2–4, до 3 запусков mesh batch за кадр, до 6 интеграций результатов, 3x3 readiness перед meshing, ArrayPool для snapshot, thread-local scratch mesher, один coalesced GPU upload на чанк, отсутствие mesh работы вне RenderDistance, CPU/GPU cache radii по схеме source и переиспользуемые dynamic buffers мобов. Подробности: `R8_RENDER_PERFORMANCE_REPORT.md`.

## R9 — скорость рендера + выбор режима

R9 дополнительно приближает hot-path к main.js: целочанковая семантика worker batch, без полного sort очереди секций, один contiguous IBO без повторного копирования, world-space shader path без лишнего ObjectToWorld, один celestial draw для солнца+луны и без Unity Directional Light. Перед запуском мира теперь выбирается Survival или Creative; в Survival полёт отключён, в Creative сохранён double-space flight. F3 показывает счётчики render/gen/mesh/upload.

Точные оставшиеся speed-gap и ограничения перечислены в `R9_RENDER_SPEED_MODE_REPORT.md`.

## R9 FIX1 — IMGUI compile fix
- В `Packages/manifest.json` включён built-in модуль `com.unity.modules.imgui`, необходимый для `MainModeSelector` и F3 `MainPerformanceHud`.
- Это устраняет CS1069 по `GUIStyle` без изменения render hot-path.

## R10 — быстрый break/place

Удаление и установка игроком больше не ждут общей очереди mesh workers. Как в `main.js`, затронутые секции перестраиваются сразу, а коррекция света идёт отдельной инкрементальной очередью с кадровым бюджетом. Убран прежний полный 3x3 relight из `SetBlock`; GPU-буферы чанка сохраняют capacity между локальными edit-upload.

## R11 — render distance + edit/submesh speed
- Исправлен Console warning `SetSubMesh ... shares part of its index buffer`: три диапазона теперь задаются атомарно через `Mesh.SetSubMeshes`.
- Дальность прорисовки: `[`/`-` уменьшить, `]`/`=` увеличить. Пресеты как в main.js: 4/6/8/12/16/20/25/30/40/50 с source-style device cap 8/12/20.
- Выбранная дальность сохраняется; F3 показывает текущее значение.
- Дополнительно уменьшены visibility/state writes, allocations при chunk upload и GC incremental lighting при частом ломании/установке блоков.

## R12 — maximum render performance
- `Task.Run` убран из generation/mesh hot-path: используются постоянные gen/mesh worker threads, ближе к WebWorker-модели main.js.
- Полная сортировка generation-кандидатов убрана; выбираются только ближайшие чанки для реально свободных workers.
- Почти полный rebuild чанка использует один общий pooled snapshot `18x18x386` вместо десятков отдельных section snapshots.
- Chunk upload staging переведён с `List<T>` на `ArrayPool` + `Array.Copy`; дальние staging buffers теперь реально возвращаются в pool.
- Vertex/index upload уведомляет MeshRenderer один раз после финального `SetSubMeshes`.
- Bounds чанков сужены до реально непустых вертикальных секций для лучшего frustum culling.
- Chunk GameObjects больше не образуют огромную runtime transform-иерархию и скрыты из Editor Hierarchy.
- Отключены Unity quality paths, которых нет в main renderer: pixel lights, realtime reflection probes, soft particles; MSAA/shadows/aniso остаются выключены.
- Проверка `validate_r12_max_render.py`: 45/45 PASS.

## R17 — предметы / инструменты / лук / offhand из main.js

Продолжен first-person renderer: все 131 исходных `item_*.png` собраны без ресемплинга в `Resources/Voxel/main_items.png`; `sL()` перенесён как пиксельная 3D-экструзия по alpha mask. Для лука перенесены отдельные `W3()/cL()` — корпус без пикселей тетивы, динамическая тетива, изгиб по charge и стрела. Перенесены transforms `b3()` для item/tool/bow, зеркальный offhand, bob/swing/equip и fishing-rod mirror. Всё остаётся direct CommandBuffer без MeshRenderer/MeshFilter. Текущий упрощённый hotbar по-прежнему блоковый; добавлены чистые API hooks, чтобы последующий перенос inventory подключал предметы без переделки renderer.

## R18 — third-person player + F5 camera из main.js

Перенесён `aR()/Qd()` third-person player path из source: точные 12 skin parts `Oh[]`, базовый и второй skin layer (+0.25 px), source UV и face shading, walk/swim/crawl/tread-water/sneak poses, head pitch, swing/bow arm pose и block в правой руке. Player добавляется в существующий direct entity batch, а held block — в отдельный block-atlas direct batch; MeshRenderer/MeshFilter не создаются.

`F5` теперь циклически переключает source camera modes `0/1/2`: first person, third-person behind, third-person front. Collision камеры использует исходные параметры `max=4`, `step=.12`, `margin=.22`; raycast ломания/установки и selection остаётся от глаз игрока и source aim direction, а first-person held pass в режимах 1/2 отключается. Camera bob убран из самой камеры: как в source, bob остаётся только у first-person held pass.

Важно для точности: `main.js` сохраняет `p.armor` и использует броню в survival/UI, но `aR()/Qd()` не рисует armor layers на third-person player. Поэтому R18 намеренно не добавляет выдуманный armor renderer. Riding/sailing позы source будут подключены вместе с переносом соответствующего ship/riding gameplay state.

## R19 — current-source transient render + survival mining (2026-09-11)

Проект повторно сверён именно с загруженными `main/meshWorker/genWorker/tex` от 2026-09-11. Добавлен direct `MainTransientRenderer`: source-порядок `terrain -> particles/drops -> entities -> crack -> water -> glass`, break particles `Yr/X7/W7`, block-drop foundation `Xe/P7/h7`, точные 10 процедурных crack stages `w0/XC/rP` и survival hold-to-mine по `Z6/zc` для уже представленного BlockId-набора. Проверка `validate_r19_transient_mining.py`: **24/24 PASS**.

Полный render всё ещё не объявляется 100%: остаются `kB` falling/TNT, `pP` boat/minecart, `tI` moving ships, `nT` fishing bobber/line, `yd` ender pearl, world arrows из `Bd`, `$B` red boxes и item-only ветка drops после подключения inventory namespace. Полная текущая матрица точности: `R19_TRANSIENT_RENDER_MINING_REPORT.md`.

## R24 — source-style renderer / anti-stutter scheduler (2026-09-11)

Renderer/streaming повторно разобран по текущему `main.js` и `meshWorker.js`. Перенесён главный принцип source hot-path: **один общий deadline кадра** для generation integration / lighting / mesh integration+upload / mesh scheduling. Generation queue ограничена `WT=10`, mesh scheduler допускает до 3 send и до 6 completed results, но прекращает работу при исчерпании frame headroom. Полный chunk mesh теперь заранее склеивается на background mesh thread и на main thread приходит уже готовым для GPU upload. Shared snapshot копирует только реально занятый вертикальный диапазон чанка. `SectionSnapshot`, `MeshBuildResult`, `MeshBuildBatch` и section render records переведены в value types, whole-chunk payload и крупные массивы переиспользуются через pools.

F3 показывает `p95/p99/max`, GC0, очереди и `stream work / deadline`. Новый аудит `Tools/validate_r24_source_render.py` — **44/44 PASS**. Подробности: `R24_SOURCE_RENDER_REPORT.md`.

## R25 — исправление CS0136 в MobAI

Исправлена ошибка Unity C# `MobAI.cs(470,126) CS0136`: в `BuildPath()` локальный индекс нового path-node переименован с `ni` в `newIndex`, потому что `ni` уже объявлен через `out int ni` в соседней ветке того же `if/else`. Логика A*, AI и оптимизации R24 не менялись. Проверки: AI 65/65, R24 source-render 44/44, R21 render-speed 46/46.

## R26 — render hot-path

R26 продолжает source-style renderer из R24/R25 и убирает лишнюю работу в каждом кадре: managed cache границ чанков, прямое source-style frustum извлечение без нормализации плоскостей, пропуск пустых draw-команд, dirty-upload 16x16 light LUT, cache `SetSubMesh`, cached mob cull radius и `half`-precision для fragment/interpolator данных как аналог `mediump` из `main.js`. Геометрия, UV, AO/light, Cull Off и порядок opaque/effects/entities/water/glass не менялись. Подробности: `R26_RENDER_HOTPATH_REPORT.md`.

## R27 — Mesh upload / anti-stutter (2026-09-12)

R27 оптимизирует оставшийся Unity-specific chunk upload path. `ChunkRender` больше не читает `Mesh.isReadable` на каждый upload: writability хранится на C# стороне. Ближнее горячее кольцо чанков (`WritableMeshRadius=3`) оставляет persistent Mesh обновляемым для edit/light rebuild без native recreation; более холодные GPU-cached чанки по-прежнему могут освобождать Unity CPU mesh copy, чтобы высокая дальность не съедала память. Если sealed mesh всё же приходится заменить, это видно в F3 как `mesh replacements`.

`Mesh.SetSubMeshes` теперь пропускается, если vertex count, три index-range и bounds не изменились. F3 дополнен `upload last/ewma/max`, KB, total uploads, replacements и timing фаз generation/light/mesh integration/schedule. В Unity Profiler добавлены маркеры `Blockcraft.Stream.*` и `Blockcraft.Render.MeshUpload`. Проверка `Tools/validate_r27_mesh_upload.py`: **31/31 PASS**. Подробности: `R27_MESH_UPLOAD_REPORT.md`.


## R28 MOB SYSTEMS

R28 expands all current main.js mob kinds with source-backed baby/breeding state, sheep colors/shearing, chicken eggs, death drops, slime split, chunk streaming persistence, stuck/pickup/world-render skeleton arrows, closer Creeper ray explosions, passive overflow cleanup, and a dedicated `main_items.png` transient item batch.

See `R28_MOB_SYSTEMS_REPORT.md` for exact source parity and the remaining inventory/save/audio/content dependencies.

## R28 — стрелы скелета по main.js

В R28 семь шагов бинарного поиска точки попадания — это не дополнительная логика Unity: такой же 7-шаговый поиск есть в `main.js`. Полёт стрелы идёт на source fixed-step 20 Гц, разбивается на шаги не длиннее 0.4 блока, а collision проверяется через source-подобные `An()/W1()` формы блоков. Застрявшая стрела освобождается только при изменении именно того блока, в который она вошла, и получает `(0,-0.5,0)`, как в исходнике.

Единственная отмеченная зависимость этого пути — `main.js` дополнительно проверяет столкновение с движущимися кораблями через `Fs(...)`; полный moving-ship collision ещё не перенесён.

## R31 — MOB BEHAVIOR SOURCE EXACT (2026-09-12)

Продолжение R30 без переноса кораблей. Исправлены оставшиеся независимые от ship-runtime отличия `main.js`: точный `vp()` при безвыходном застревании, 0.4-секундный shark `Mp()` target-cache, source random-flow спавна `fd/eu/Xp/D2/Wp/cd/$p`, pumpkin-vision hook Enderman, source armor formula, `$J()` Creeper blast resistance, `oR()` урон взрывом другим мобам и точная точка дропа шерсти `y+0.6`.

Проверки: R20 65/65, R21 46/46, R24 44/44, R26 39/39, R27 31/31, R28 109/109, R29 109/109, R30 132/132, R31 73/73 PASS. Корабли в этой ревизии не изменялись.


## R32 — WATER + EDIT LIGHT FIX (2026-09-12)

- Перенесён source water-flow для обычного мира: WATER/FLOW3/FLOW2/FLOW1, 200 мс tick, 400+80 queue pull, cap 120.
- Удаление блока рядом с рекой теперь ставит соседнюю воду в очередь и вода растекается в открывшуюся ячейку.
- Потоки рисуются с source-высотами 0.875/0.65/0.45/0.25.
- Исправлена чёрная stale-light/AO грань после удаления блока: incremental skylight получает radius-16 frontier и пересчитывается до immediate remesh.
- Корабли не изменялись.

## R35 — BIOME PARITY + SPEED (2026-09-13)

Биомный/deepslate генератор сохранён по алгоритмам source, но hot-path оптимизирован на C#: один biome sample на колонку переиспользуется поверхностью, водой, растительностью и density lattice; добавлен быстрый thread-local cache. Формулы климата, multi-noise границы, океаны/реки/берега, cave-biomes и растительность не упрощались. Подробности: `R35_BIOME_PARITY_SPEED_REPORT.md`.

## R36 — WORLDGEN STRUCTURES, C# ONLY (2026-09-13)

Добавлен недостающий non-ship worldgen-слой: desert wells, desert pyramids, pillager outposts, ruined portals и deepslate mineshafts. Весь production-код написан на C#; JS-файлы в Unity-проект не входят. `main/genWorker` используются только как внешний эталон для сверки алгоритмов.

Исправлена точная JS Number -> ToInt32 семантика structure RNG, поэтому координаты кандидатов совпадают с эталонным алгоритмом. Добавлены source T2 clear-zones: деревья не растут в зоне будущих pyramid/outpost/portal/well; в C# эти зоны предвычисляются один раз на чанк вместо повторной проверки на каждый tree candidate.

Старые BlockId не сдвигались. Новые структурные блоки подключены к существующим mesher/collision/mining/TNT путям. Корабельные структуры и ship gameplay намеренно исключены.

Проверка `Tools/validate_r36_structures.py`: **75/75 PASS**. Общая матрица R20–R36: `STATIC_VALIDATION_R36_COMBINED.txt`.

## R37 BIOME DECOR — 2026-09-13

- Исправлена семантика Cherry Grove: натуральное дерево использует дубовый ствол и смесь azalea/flowering azalea leaves по исходному C2().
- Lush caves теперь используют отдельный FloweringAzaleaLeaves при source-пороге 0.06.
- Новый ID добавлен только в конец enum; старые numeric BlockId не сдвинуты.
- Для plant/tree pass переиспользуются pooled 16x16 aquifer/TreeKind caches без изменения результата генерации.
- В production-проекте нет JavaScript-файлов; main/genWorker используются только как внешний эталон.
- Корабли по-прежнему исключены.

## R40 — СНАЧАЛА МИР, ПОТОМ ИГРА (C# ONLY, 2026-09-13)

Исправлен порядок старта по `main.js`. Игрок больше не получает управление, пока мир только стримится вокруг него. Сначала вокруг фиксированной spawn-позиции полностью генерируется стартовый квадрат радиуса 4, затем финализируется свет, затем строятся и загружаются GPU-меши радиуса 3. Только после этого включаются `MainPlayerController`, течение времени и обычный mob tick.

Во время загрузки показывается непрозрачный `Generating world...` экран с source-разбиением прогресса 50% generation / 15% light / 35% mesh. После открытия gameplay обычный быстрый streaming продолжает достраивать дальние чанки. JS в Unity-проект не добавлялся. Подробности: `R40_SOURCE_STARTUP_PRELOAD_REPORT.md`.

## R41 — CONTINUE + SPARSE WORLD SAVE (C# ONLY, 2026-09-13)

Продолжен source-style lifecycle после R40. Теперь новый мир после завершения startup preload получает базовый save, а `Continue` восстанавливает seed, позицию/поворот игрока, creative flight, время суток, sparse block edits, компактных мобов и список уже засеянных fauna-чанков. Saved pose и edits применяются **до** R40 startup generation/light/mesh gate, поэтому управление снова включается только после подготовки уже восстановленной области.

Полные чанки не сериализуются: сохраняются только реально изменённые клетки в source-координатах/source-meta. Это сохраняет небольшой размер файла и детерминированную генерацию. JS в Unity-проект не добавлялся; корабли намеренно не реализуются. Подробности: `R41_SOURCE_WORLD_SAVE_REPORT.md`.

## R42 — SAFE RENDER HOT-PATH (C# ONLY, 2026-09-13)

Проверен CPU/render hot-path без изменения визуального результата. Разделены command-buffer invalidation и chunk-candidate invalidation: появление/исчезновение particles/entities/crack/selection больше не заставляет повторно сканировать весь `(2R+1)^2` словарь чанков. Отдельный candidate revision пересобирает список только при смене центра/render distance или реальном структурном изменении GPU-чанка.

Entity batch получил render-version cache: при неизменных camera/model state в first-person предыдущий GPU batch переиспользуется без повторного frustum/geometry/upload; при движении камеры frustum по-прежнему проверяется, но geometry/upload пропускается, если точный visible sequence и версии моделей не изменились. Любое движение, анимация, свет, hurt/fuse/squash/state автоматически инвалидируют кэш. Third-person path остаётся динамическим.

Дополнительно кэшируются точные AABB min/max чанков, убраны два дублирующих render invalidation и скрытый F3 больше не сортирует percentile-buffer. Шейдеры, Cull Off, вода/стекло, vertex format, AO/light, render order, worldgen/AI/save не менялись. Проверки: **R42 50/50**, полный regression **1293/1293 PASS**. Подробности: `R42_RENDER_SAFE_OPTIMIZATION_REPORT.md`.

## R43 — ADAPTIVE 16-BIT CHUNK INDICES (C# ONLY, 2026-09-13)

Продолжена безопасная оптимизация рендера после R42. Геометрия, UV, шейдеры, порядок opaque/water/transparent и draw-order не менялись. Для chunk mesh теперь автоматически используется `UInt16` index buffer, если merged mesh содержит не более 65 535 вершин; более плотные чанки автоматически остаются на `UInt32`.

Full-chunk worker сразу пакует подходящие индексы в pooled `ushort[]` в background thread, поэтому обычный streaming не получает дополнительную main-thread конверсию. Partial edit rebuild использует pooled `ushort[]` только для подходящих чанков. Это вдвое уменьшает индексную часть GPU upload/VRAM/bandwidth у таких чанков без изменения topology. F3 показывает `idx 16/32`, сохранённые KB и счётчики `idx16/32`.

Используется консервативный лимит 65 535 вершин, чтобы не использовать максимальный UInt16 index `0xFFFF` на API/GPU, где он может быть специальным значением. Dense chunks остаются на 32-bit path. Проверки: **R43 44/44**, полный regression **1337/1337 PASS**. Подробности: `R43_RENDER_INDEX_BANDWIDTH_REPORT.md`.

## R49/R50 — SHIPWRECK + MOVING SHIP RUNTIME (2026-09-17)

R49 вернул отсутствовавший source shipwreck-pass: 20 исходных шаблонов / 9111 блоков, source spacing/separation/salt, beach/underwater selection, сундуки, `SHIP_WHEEL`, `OAK_DOOR` и его texture fallback. Движущееся судно использует тот же `ChunkMesher`, что и мир, но хранит persistent GPU mesh; движение/поворот меняют только transform. Вода судна остаётся отдельным alpha-pass между world water и glass.

R50 продолжает `Q6/P6/hc/BB/Os/v6/A7/g6/Ns/EE/Ss/w7/Lc`: отделение корпуса до 3000 блоков, управление, collision yaw->X->Z, плавучесть, герметичный воздух и постепенное затопление, dynamic water carve, сохранение кораблей, локальный raycast/collision, ломание/установка блоков на пришвартованном судне, двери/калитки/люки и перенос мобов вместе с палубой.

Текущий gate: **R50 59/59**, **R49 current 30/30**, **R49 shipwreck/render 41/41**, **R48 inventory/tools 67/67**, **R32 water/light 31/31**. Подробности: `R50_SHIP_RUNTIME_PARITY_REPORT.md` и `STATIC_VALIDATION_R50_COMBINED.txt`.

Важно: в среде сборки этой ревизии отсутствовал Unity Editor/компилятор C#, поэтому эти результаты — source/static parity, а не заявление об успешно выполненном PlayMode/Windows build или измеренном FPS.

## R52 — СУНДУКИ, ПЕЧИ И КРАФТ В ОБЫЧНОМ МИРЕ (2026-09-18)

По просьбе продолжение сделано **без новой логики движущегося корабля**. Добавлены обычные сундуки на 27 слотов и двойные сундуки на 54, source-направление/left-right metadata, выпадение содержимого и сохранение. Печь получила input/fuel/output, 10-секундную плавку, откат progress 0.3/с, source fuel timings, lava-bucket return, поддержанные smelting mappings и сохранение состояния.

Добавлен survival crafting: 2x2 из инвентаря, 3x3 через верстак, shaped/mirrored/shapeless matcher, перенос остатков сетки обратно в инвентарь и набор рецептов из `main.js`, которые уже представимы существующими BlockId/item IDs. Рецепты, требующие ещё не перенесённых ID, намеренно не подменяются похожими блоками и остаются на следующий non-ship проход. Рецепт штурвала/новая ship-container интеграция в R52 не добавлялись.

`MainShipRuntime.cs` и `MainMovingShipRenderer.cs` побайтно совпадают с R51. Актуальная совместимая матрица: **316/316 PASS**. Подробности: `R52_STATIC_FUNCTIONAL_BLOCKS_REPORT.md` и `STATIC_VALIDATION_R52_COMBINED.txt`.

Как и для R50/R51, в этой среде нет Unity Editor/компилятора C#, поэтому PlayMode/платформенная сборка здесь не заявляется как выполненная.

## R57 — НАЗЕМНЫЕ МОБЫ + ТУМАН/STREAMING REGRESSION (2026-09-19)

Исправлен source-порядок одноразового fauna-spawn: `OnChunkLoaded/eu()` теперь вызывается в момент публикации сгенерированного чанка, сразу после `chunks[...] = chunk`, а не после lighting. Шансы/лимиты не повышались: initial passive остаётся 10%, natural passive — раз в 20 секунд днём, основание — только GRASS, hostile/slime rules не менялись.

Исправлен резкий синий обрыв мира при старте/стриминге. Целевой render distance больше не используется для fog до того, как этот квадрат реально опубликован на GPU. Добавлен event-driven `PublishedRenderRadius`; временно fog следует фактически готовому непрерывному горизонту и автоматически возвращается к точной формуле `main` (`R*16-26 / R*16-2`) при полной готовности. Генерация, culling и качество мира этим не уменьшаются.

F3 дополнен фактическими land-spawn attempts/success/reject counters, `rd target/published` и временем `entity-batch`. Это позволяет отличить отсутствие наземных мобов от renderer-проблемы и проверить, дают ли многочисленные рыбы spike. Подробности: `R57_SPAWN_FOG_STREAMING_REGRESSION_REPORT.md`.

## R67_FRAME_PACING_F_FLIGHT
- F: включить/выключить полёт в Creative; двойной Space остаётся.
- Уменьшены редкие CPU-пики: memoization точных A* collision probes, повторное использование entity index topology, более частый yield chunk-light по общему frame deadline, меньше временных аллокаций при mob save capture.
- Fog/direct renderer/mesher/worldgen/biomes/R66 streaming и корабельные файлы не изменены.

## R70 — source-nearest streaming / аудит тумана

R70 убирает camera-facing/predictive/adaptive приоритеты из очередей стриминга и возвращает фактический порядок generation/mesh к `main.js`: ближайшие чанки по квадрату расстояния от текущего чанка игрока, generation window 10, mesh только после готового 3x3 соседства, до 3 новых chunk mesh jobs за проход. Fog не менялся: `R*16-26 .. R*16-2`, под водой `4..18`.

На RD=20 source fog равен `294..318`. Если F3 показывает, например, `gpu-ready 4`, готовый непрерывный terrain заканчивается примерно на 64 м, поэтому до fog band геометрия ещё не дошла. F3 R70 теперь явно показывает `ready~Xm`.

## R78 — FLUID RELIGHT ROOT FIX (2026-09-20)

R77 frame-pacing fuse **не используется**. После сравнения с исходной загруженной R74 и быстрым R75 установлено, что просадку после R76 вызывал не сам source-style frame budget, а неверно дорогой путь deferred skylight при массовом растекании воды через новый cross-chunk seam repair.

R32 добавил radius-16 skylight frontier shell для корректного **открытия** света после удаления/ослабления препятствия. R76 AIR->FLOW ошибочно запускал тот же shell для каждого нового водяного блока, хотя вода не открывает свет, а наоборот увеличивает attenuation с 1 до 2. Один shell делает до 6146 world-light sample checks; полный source water tick на 120 изменений мог запускать до 737520 заведомо лишних shell-checks, после чего F3 показывал `light ~34 ms`.

R78 передаёт deferred-light старый/новый BlockId+meta и классифицирует изменение света по фактической семантике `DirectSkyPasses / LightCost / CanLightPass`:
- AIR -> WATER/FLOW: skylight пересчитывается, но expensive frontier shell не запускается;
- WATER/FLOW -> AIR: shell сохраняется, потому что свет действительно открывается;
- AIR -> LAVA: skylight вообще не пересчитывается (проход/attenuation не изменились), block-light emission всё равно обновляется;
- LAVA -> OBSIDIAN/COBBLE: skylight уменьшается без opening-shell;
- STONE -> AIR и другие реальные открытия сохраняют R32 shell.

Cross-chunk seam repair R76 сохранён и по-прежнему только seed-queue'ит штатный water/lava solver. Renderer, mesher, shaders, AI, biome/worldgen и source-style streaming budget R74/R75 не менялись. Подробности: `R78_FLUID_RELIGHT_ROOT_FIX_REPORT.md`.
