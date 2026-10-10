# parity-math — побитовое совпадение математики порта с V8 (Node 22)

`Assets/VoxelForge/Scripts/Core/JsMath.cs` — чисто управляемый порт тех функций, которые V8 12.4 (Node 22) на самом деле
выполняет для `Math.pow/sin/cos/tan/log/log10/atan/atan2/round/hypot`. Порт не выделяет память и не использует `unsafe`.
`System.Math` вызывает libm платформы (glibc, MSVC CRT, bionic, Apple), а она в последнем бите отличается и от V8,
и между платформами. Генератор мира использует только `JsMath`, поэтому мир получается одинаковым на всех устройствах и совпадает с JS.

## Что именно повторяется (проверено по исходникам V8 12.4.254.21 и экспериментально)

* `Math.pow`, `log`, `log10`, `atan`, `atan2`, `tan`: `src/base/ieee754.cc`, это fdlibm с правками V8 (`pow(±1, ±Inf) = NaN` и т. д.).
* `Math.sin`/`cos`: в Node 22 это **fdlibm**. Node собирается через gyp без `v8_use_libm_trig_functions`, флага
  `--use-libm-trig-functions` нет в `node --v8-options`. В Chrome (clang) по умолчанию `v8_use_libm_trig_functions = is_clang`,
  то есть sin/cos берутся из копии glibc (`third_party/glibc`), так что **Chrome и Node могут отличаться в последнем бите sin/cos**.
  Эталон здесь — Node.
* `Math.round`: `Float64Round` из CSA/TurboFan: `r = ceil(x); if (r - 0.5 > x) r -= 1`. Это не `floor(x + 0.5)`:
  результаты различаются на `0.49999999999999994`, на |x| ≥ 2^52 и в знаке нуля.
* `Math.hypot`: Torque-builtin `MathHypot`. Он нормирует аргументы на максимум и суммирует квадраты по Кэхэну.
  Это не `sqrt(a*a+b*b)`.
* Интерпретатор (Ignition) и TurboFan вызывают одни и те же C-функции. `gen.js` это проверяет: каждый 1024-й результат
  пересчитывается в функции, которой запрещена оптимизация.

## Запуск

Нужны `node` (22+), `mcs` и `mono`. Команды запускаются из папки `VoxelForgeUnity`.

```sh
# 1) математика: по 5 млн образцов на функцию (+ ~1 млн фиксированных пар для pow/atan2/hypot), ~1 мин
mcs -langversion:7.2 -optimize+ -out:/tmp/MathParity.exe Tools/parity-math/MathParity.cs Assets/VoxelForge/Scripts/Core/JsMath.cs
node Tools/parity-math/gen.js --n=5000000 | mono /tmp/MathParity.exe        # итог: "math parity: OK"
node Tools/parity-math/gen.js --n=1000000 --fns=pow,sin --seed=2 > v.txt     # векторы можно сохранить в файл…
mono /tmp/MathParity.exe v.txt                                               # …и проверить потом
mono /tmp/MathParity.exe --bench                                             # нс/вызов JsMath против System.Math

# 2) генератор мира: эталонный createWorldGenKernel из pretty.js против C# WorldGenKernel
sh Tools/parity-math/worldgen/run.sh <путь/к/pretty.js> [outDir] [--quick] [сид ...]   # итог: "worldgen parity: OK"
```

* Формат векторов: одна строка на образец, `<fn> <a> [<b> [<c>]] <ожидаемое>`. Все значения записаны как 16 hex-цифр битов
  IEEE-754. NaN считается совпадением с любым NaN, остальное сравнивается побитово (±0 различаются).
* Входные данные `gen.js`: спецзначения и граничные старшие слова, на которых ветвится fdlibm; случайные битовые шаблоны;
  равномерные диапазоны; субнормальные числа; огромные аргументы (ветка Payne–Hanek, |x| > 2^19·π/2); точки вблизи k·π/2 ± ulp;
  целые и полуцелые показатели; отрицательные основания; |y| > 2^31 при x ≈ 1; края переполнения и антипереполнения
  (z ≈ 1024 и −1075); точные наборы аргументов генератора мира (углы колец долин i·π/4, `sin(i/(steps−1)·π)` для оврагов,
  `log10(n − 0.01)`, `pow(10, k)`); диапазоны `pow(pv·0.5+0.5, 1.4)` и `pow(mount, 1.15)`; блуждание угла оврага.
* Колонка `System.Math-mismatch` дана только для справки: она показывает, насколько libm платформы расходится с V8.
* `worldgen/run.sh` по умолчанию проверяет 8 сидов (12345, −5, 7, 0, 1, 2147483647, −2147483648, −1640531527).
  Для каждого сида берутся 84 чанка: 3×3 у начала координат, случайные ближние и дальние (до ±1.5 млн чанков),
  центры пирамид, аванпостов, порталов, кораблей, шахт, оврагов и колодцев, а также чанк регрессионной точки
  (−45791, −9148). Сравниваются:
  блоки (`.bin`); мета (`.meta`); биом, высота и поверхность вместе с битами `origH/factor/mAmp/peak` (`.bio`);
  3004 точки (`.an`, через `featureInfoAt`, `landPlantInfoAt`, `dryAt`, `terrainDensity` и все поля `columnInfo` в hex-битах);
  планы оврагов для 120×120 ячеек (`.rav`: все узлы x/z/r в hex-битах) и `MAIN_VALLEY_OFFSETS`.
  `--quick`: 400 точек, 3 дальних чанка, 40×40 ячеек оврагов. Полный прогон занимает ~7 мин, быстрый для двух сидов — ~45 с.
* `B.json` (имя блока → id) пишет C#-сторона (`WorldgenParity.exe names`), и обе стороны получают одинаковые id.
  `wg.js` сам находит `function createWorldGenKernel(` в `pretty.js`, подставляет данные `/*EXTRACTED …*/` из
  `Resources/VoxelForge` и добавляет в возвращаемый объект служебные экспорты (`planRavine`, `structureCandidate`, …).

## Файлы

| файл | что делает |
|---|---|
| `gen.js` | генератор векторов (эталон: `Math.*` текущего node) |
| `MathParity.cs` | проверка `JsMath` по векторам и бенчмарк `--bench` |
| `worldgen/run.sh` | сборка `WorldgenParity.exe` и прогон обеих сторон для каждого сида, затем сравнение |
| `worldgen/wg.js`, `worldgen/WorldgenParity.cs` | JS- и C#-стороны (режимы `find`/`names`, `chunks`, `an`, `rav`) |
| `worldgen/points.js`, `worldgen/compare.js` | детерминированные списки чанков и точек, побайтовое сравнение `js_*` и `cs_*` |

## Замечания

* Эталон — Node x64. Node и Chrome на arm64 (clang с `-ffp-contract=on`) могут слить `a*b+c` в FMA внутри самого V8 и дать
  другие последние биты. Это свойство браузера: в JS-оригинале мир тоже отличается между такими платформами.
* В IL2CPP/C++ тоже возможна FMA-контракция. IL2CPP пишет каждую операцию IL отдельным оператором, поэтому слияния
  в пределах одного выражения практически нет. Чтобы проверить конкретную сборку, запустите `MathParity` под этой средой выполнения.
