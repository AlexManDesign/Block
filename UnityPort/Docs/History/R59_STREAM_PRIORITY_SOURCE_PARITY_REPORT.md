# R59 — STREAM PRIORITY / SOURCE ORDER PARITY

Дата: 2026-09-20  
Target: Unity 2022.3.62f3

## Что было не так в R58

Fog distance в R58 уже соответствовал `main.js` и сам по себе не зависел от направления движения. Однако pending generation в Unity оставался FIFO: если игрок быстро перелетал через несколько чанков, ещё не начатые jobs, поставленные около старого центра, продолжали выбираться раньше новых чанков около текущего положения. Визуально это выглядело как будто haze/horizon остаётся позади движения, хотя проблема была не в формуле fog, а в порядке фоновой генерации.

В `main.js` generation queue `Cu` переупорядочивается через `jw(qo())` по квадрату расстояния до текущего chunk-center. R59 восстанавливает эту семантику без directional bias/look-ahead.

## Исправление

### `VoxelWorld.cs`

- FIFO `ConcurrentQueue<ChunkCoord>` для generation pending заменён на маленький bounded pending-list (source WT=10).
- Каждый persistent generation worker при получении следующей работы выбирает ближайший pending chunk относительно **текущего** player chunk-center.
- Уже выполняющийся worker не отменяется — как и source worker; переупорядочиваются только ещё не начатые jobs.
- Main thread по-прежнему только публикует задания; `Generator.Generate()` остаётся исключительно в dedicated worker threads.
- Никакого look-ahead, velocity bias или специального «рендерить только вперёд» не добавлено.
- Equal-distance generation order приведён к source stable order: X outer → Z inner.
- Mesh scheduler при равной дистанции больше не зависит от порядка `Dictionary`; tie-break теперь соответствует source X → Z order.
- Direct visible/render scan и wake-up upload scan используют тот же X outer → Z inner order, что `main.js`.

## Что намеренно НЕ менялось

Чтобы не сломать достигнутую скорость R58, побайтно не изменены:

- `ChunkMesher.cs`
- `VoxelLighting.cs`
- `ChunkGenerator.cs`
- `ChunkRender.cs`
- `MainChunkDirectRenderer.cs`
- `VoxelRenderEnvironment.cs`
- `VoxelOpaque.shader`
- `VoxelTransparent.shader`
- `VoxelWater.shader`
- `MobAI.cs`
- `MobSpawner.cs`

Следовательно R59 не меняет mesh topology, lighting propagation, GPU upload implementation, fog formula, shader math, biome/worldgen formulas или mob AI/spawn logic.

## Fog остаётся exact source

- air near = `RenderDistance * 16 - 26`
- air far = `RenderDistance * 16 - 2`
- underwater = `4 .. 18`
- vertex distance = radial camera/world distance
- `PublishedRenderRadius` остаётся только диагностикой и не управляет fog.

## Дополнительный аудит расхождений

Во время R59 дополнительно проверены source/runtime hot paths. Подтверждённые следующие крупные **не-корабельные** слои, которые ещё нельзя считать полностью перенесёнными:

1. Полный source block-lifecycle/random-tick слой (`tB/qP/oh/xP/dT/Eh`): crop growth, farmland state, grass spread, sapling/tree timers, sugar-cane/cactus growth, fire lifecycle и связанные persistent timers требуют отдельного полного порта.
2. Creative/spawn-egg UI и часть item/UI surface остаются неполными относительно `main`.
3. Часть рецептов по-прежнему не может быть выражена до добавления отсутствующих source item/block IDs; R52 специально не подменял их похожими ID.
4. Общий звуковой слой `main` ещё не имеет полноценной Unity parity.

Эти пункты не маскируются как «готовые». R59 касается только подтверждённого streaming-order расхождения и source-order детерминизма рендера.

## Валидация

- R59 stream priority/source parity: **47/47 PASS**
- dry spawn regression: **58/58 PASS**
- R54 architecture: **63/63 PASS**
- R56 mobs: **76/76 PASS**
- mob collision: **109/109 PASS**
- mob movement: **132/132 PASS**
- mob behavior: **73/73 PASS**
- current renderer: **30/30 PASS**
- water/light: **31/31 PASS**
- survival/items/TNT: **71/71 PASS**
- static chest/furnace/crafting: **87/87 PASS**
- compile hotfix: **8/8 PASS**
- scene setup: **9/9 PASS**

Актуальная совместимая матрица: **794/794 PASS**.

## Ограничение

Unity Editor/Roslyn отсутствуют в среде выполнения, поэтому реальный PlayMode frame capture здесь не выполнялся. R59 сохраняет существующий R58 render/mesh/light hot path и меняет только выбор следующего pending generation job и deterministic tie-order.
