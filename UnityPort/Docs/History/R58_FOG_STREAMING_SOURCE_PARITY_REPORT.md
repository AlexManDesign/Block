# R58 — FOG / STREAMING SOURCE PARITY

Дата: 2026-09-19  
Target: Unity 2022.3.62f3

## Причина регрессии R57

R57 ошибочно связал воздушный fog distance с `PublishedRenderRadius` — текущим непрерывным кольцом уже загруженных GPU-мешей. При быстром движении/полёте это кольцо могло временно уменьшаться, поэтому fog резко приближался к камере. В `main.js` такой связи нет.

Дополнительно Unity feeder генерации выполнялся по 120-ms polling timer. В `main.js` `al(frameDeadline)` вызывается каждый кадр в chunk phase, а тяжёлая генерация остаётся worker-работой с ограниченным pending window.

## Точная source-семантика fog

В `main.js`:

- воздух: `FogNear = RENDER_R * 16 - 26`
- воздух: `FogFar  = RENDER_R * 16 - 2`
- вода: `FogNear = 4`
- вода: `FogFar = 18`
- fragment shader использует линейный distance fog: `clamp((dist-near)/(far-near), 0, 1)`.

Следовательно, при неизменном Render Distance fog не меняется от движения, скорости полёта, загрузки чанков или состояния GPU upload.

Примеры:

- RD=6: 70 → 94 blocks
- RD=4: 38 → 62 blocks

## Исправление R58

### `VoxelRenderEnvironment.cs`

Удалена зависимость fog от `EffectiveFogRenderDistance` / `PublishedRenderRadius`. Формулы теперь непосредственно соответствуют `main.js`. Никакого smoothing/Lerp не добавлялось: source fog сам по себе стабилен.

### `VoxelWorld.cs`

- удалён 120-ms `streamTimer`;
- `Stream(deadlineMs)` вызывается каждый кадр в chunk phase;
- feed выполняется после integrate-generated и до lighting/mesh, как порядок `al(i) -> G4(i) -> ol(i)` в source;
- генерация остаётся в persistent worker threads;
- pending generation остаётся bounded (`MaxPendingGeneration`), поэтому каждый кадр не запускает неограниченную работу;
- nearest-first offsets заранее отсортированы и переиспользуются;
- общий frame deadline используется только как cooperative scheduling boundary, а не как снижение качества;
- `PublishedRenderRadius` сохранён только как диагностика F3 и не влияет на rendering/fog.

### `MainPerformanceHud.cs`

HUD теперь подписывает показатель как `rd target/gpu-ready`, чтобы diagnostic GPU frontier не выглядел как фактическая дальность тумана.

## Что НЕ менялось

Побайтно относительно R57 сохранены:

- `ChunkMesher.cs`
- `VoxelLighting.cs`
- `ChunkGenerator.cs`
- `MobAI.cs`
- `MobSpawner.cs`
- `VoxelOpaque.shader`
- `VoxelTransparent.shader`
- `VoxelWater.shader`
- `MainEntityBatchRenderer.cs`

Следовательно, R58 не меняет worldgen, biome algorithms, lighting propagation, mesh geometry, mob AI/spawn probabilities или entity batching.

R57 fix публикации fauna-on-chunk-publish также сохранён.

## Валидация

- R58 fog/streaming source contract: **40/40 PASS**
- R56 mob lifecycle/render: **76/76 PASS**
- current renderer: **30/30 PASS**
- water/light: **31/31 PASS**
- mob collision: **109/109 PASS**
- mob movement: **132/132 PASS**
- mob behavior: **73/73 PASS**
- survival/items/TNT: **71/71 PASS**
- static chest/furnace/crafting: **87/87 PASS**
- compile hotfix: **8/8 PASS**
- scene setup: **9/9 PASS**

Выбранная актуальная совместимая regression/source-parity матрица: **666/666 PASS**.

Проверено дополнительно:

- production JavaScript: 0
- obvious `var a=..., b=...` CS0819 pattern: 0
- production C# delimiter structure: PASS

## Ограничение проверки

В среде выполнения отсутствует Unity Editor/Roslyn, поэтому фактический PlayMode frame capture здесь не выполнялся. R58 исправляет установленное source-расхождение: fog больше физически не может менять near/far из-за движения по миру; при быстром полёте догрузку теперь обязан догонять streaming pipeline, а не визуальный fog.
