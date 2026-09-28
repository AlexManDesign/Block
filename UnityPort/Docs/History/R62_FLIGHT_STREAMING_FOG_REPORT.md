# R62 — FLIGHT STREAMING / FOG CATCH-UP

Дата: 2026-09-20  
Target: Unity 2022.3.62f3  
Canonical behavior source: `main(20260919-202006).js`

## Что было проверено

Жалоба: при быстром creative-полёте дальний край мира может открываться раньше, чем staged pipeline успевает довести новые чанки до GPU. Визуально это выглядит как «туман не успевает рисоваться» — появляется чистый фон/пустой горизонт до того, как новая геометрия опубликована.

Проверены два независимых механизма: формула fog и фактическая пропускная способность background streaming.

## Найденная причина №1 — worker count Unity был ниже main.js

`main.js` использует:

`GI = max(2, min(4, hardwareConcurrency - 1))`

и передаёт один и тот же `GI` обоим пулам:

- generation workers: `YT(..., GI, ...)`
- mesh workers: `xT(..., GI)`

До R62 Unity использовал более консервативное `(cores - 2) / 2`.

Примеры:

| logical processors | main.js gen/mesh | Unity R61 gen/mesh | Unity R62 gen/mesh |
|---:|---:|---:|---:|
| 4 | 3 / 3 | 1 / 1 | 3 / 3 |
| 6 | 4 / 4 | 2 / 2 | 4 / 4 |
| 8 | 4 / 4 | 3 / 3 | 4 / 4 |
| 16 | 4 / 4 | 4 / 4 | 4 / 4 |

R62 восстанавливает source worker policy. Потоки остаются `BelowNormal`, а generation/meshing по-прежнему не выполняются на Unity main thread.

`MaxPendingGeneration = 10` и nearest-current-center priority из R59 сохранены.

## Найденная причина №2 — shader fog не может скрыть отсутствующий chunk

Базовая формула `main.js` остаётся:

- air near = `RENDER_R * 16 - 26`
- air far = `RENDER_R * 16 - 2`
- underwater = `4 .. 18`

Проблема не в математике shader fog: отсутствующий GPU chunk вообще не создаёт fragments, поэтому шейдеру нечего смешивать с fog color. Если GPU-ready frontier временно ближе `fogFar`, за краем виден clear color раньше полного затуманивания.

## R62 flight-only safety horizon

При обычной игре и когда streaming успевает R62 использует точные source near/far без изменений.

Только когда одновременно выполняются условия:

1. player находится в creative flight;
2. initial world уже готов;
3. `PublishedRenderRadius < RenderDistance`;

включается временный safety horizon:

- `safeFar = max(14, PublishedRenderRadius * 16 - 2)`;
- `fogNear = max(0, fogFar - 24)` — сохраняется исходная ширина fog transition 24 блока;
- при уменьшении GPU-ready frontier туман сдвигается внутрь немедленно, чтобы не показать дыру;
- при появлении новых GPU rings fog расширяется наружу через `MoveTowards(..., 96 blocks/sec)`, чтобы горизонт не пульсировал;
- после catch-up возвращается точный source target;
- underwater 4..18 не меняется.

Пример RD=6:

- source/caught-up: `70 .. 94`;
- если GPU-ready radius=3: временно `22 .. 46`;
- после догрузки плавно возвращается к `70 .. 94`.

Это намеренное улучшение относительно `main.js`: baseline формула source сохранена, но Unity не показывает незатуманенный void, если собственный staged GPU pipeline временно отстал.

## F3 диагностика

В F3 строке теперь видно:

- `rd target/gpu-ready`;
- фактический `fog near..far`;
- `SAFE`, когда flight catch-up активен, иначе `SRC`;
- `workers g/m`.

По этой строке можно сразу отличить fog problem от generation/mesh/upload backlog.

## Production diff R61 -> R62

Изменены ровно 3 production-файла:

1. `Assets/BlockcraftPort/Rendering/VoxelWorld.cs`
2. `Assets/BlockcraftPort/Rendering/VoxelRenderEnvironment.cs`
3. `Assets/BlockcraftPort/Rendering/MainPerformanceHud.cs`

Побайтно не менялись `ChunkMesher`, `ChunkRender`, `VoxelLighting`, `ChunkGenerator`, `BiomeGenerator`, `MobAI`, `MobSpawner` и world voxel shaders.

Корабельные runtime/render/generation/shader файлы также побайтно не менялись относительно R61.

## Валидация

- R62 flight streaming/fog: **54/54 PASS**
- R61 lifecycle/no-ship: **105/105 PASS**
- R60 render/mod/gameplay: **80/80 PASS**
- R56 mobs: **76/76 PASS**
- R55 dry spawn: **58/58 PASS**
- R51 survival/items/TNT: **71/71 PASS**
- R49 current renderer: **27/27 PASS**

Selected matrix: **471/471 PASS** (наборы частично пересекаются).

Старый R58/R59 assertion «fog никогда не зависит от GPU-ready radius» теперь намеренно superseded только для указанного flight-backlog состояния. Все остальные source fog constants сохранены.

## Ограничение проверки

В среде нет Unity Editor/Roslyn, поэтому фактический PlayMode GPU/frame capture не выполнялся. Статически подтверждены source worker count, fog contracts, byte-lock hot path, regression suites и отсутствие изменений корабельной подсистемы. Для реального теста во время полёта открыть F3: если streaming отстаёт, должен появиться `SAFE`, а после catch-up снова `SRC`.
