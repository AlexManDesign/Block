#!/bin/sh
# Worldgen parity: reference createWorldGenKernel (pretty.js, node) vs C# WorldGenKernel on identical chunks/points.
# Usage: sh Tools/parity-math/worldgen/run.sh <pretty.js> [outDir] [--quick] [seed ...]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../../.." && pwd)"
PRETTY="$1"; shift || true
[ -n "$PRETTY" ] && [ -f "$PRETTY" ] || { echo "usage: sh run.sh <pretty.js> [outDir] [--quick] [seed ...]"; exit 2; }
OUT="${TMPDIR:-/tmp}/vf-parity-worldgen"
case "$1" in --*|"") ;; *[!0-9-]*) OUT="$1"; shift ;; esac
PTS=3000; FAR=15; RAV=60
if [ "$1" = "--quick" ]; then PTS=400; FAR=3; RAV=20; shift; fi
SEEDS="$*"; [ -n "$SEEDS" ] || SEEDS="12345 -5 7 0 1 2147483647 -2147483648 -1640531527"
RES="$ROOT/Assets/VoxelForge/Resources/VoxelForge"
rm -rf "$OUT/data"; mkdir -p "$OUT/build" "$OUT/data"
echo "== C#: building WorldgenParity.exe"
find "$ROOT/Assets/VoxelForge/Scripts" -name '*.cs' > "$OUT/build/files.rsp"
mcs -langversion:7.2 -optimize+ -target:exe -nowarn:0162,0168,0219,0414,0649,0169,0067,0618 -r:System.Core.dll \
    -out:"$OUT/build/WorldgenParity.exe" "$ROOT/Tools/compile-check/UnityStubs.cs" "$HERE/WorldgenParity.cs" @"$OUT/build/files.rsp"
CS="mono $OUT/build/WorldgenParity.exe $RES"
JSW="node $HERE/wg.js $PRETTY $RES $OUT/build/B.json"
$CS names "$OUT/build/B.json"
for S in $SEEDS; do
  echo "== seed $S"
  $JSW find "$S" "$OUT/build/struct_$S.txt"
  node "$HERE/points.js" chunks "$S" "$OUT/build/struct_$S.txt" "$OUT/build/chunks_$S.txt" "$FAR"
  node "$HERE/points.js" pts "$S" "$PTS" "$OUT/build/pts_$S.txt"
  echo "   $(wc -l < "$OUT/build/chunks_$S.txt") chunks, $(wc -l < "$OUT/build/pts_$S.txt") points, ravine cells $((RAV * RAV * 4))"
  $JSW chunks "$S" "$OUT/build/chunks_$S.txt" "$OUT/data" &
  $CS chunks "$S" "$OUT/build/chunks_$S.txt" "$OUT/data" &
  $JSW an "$S" "$OUT/build/pts_$S.txt" "$OUT/data/js_$S.an" &
  $CS an "$S" "$OUT/build/pts_$S.txt" "$OUT/data/cs_$S.an" &
  $JSW rav "$S" "$RAV" "$OUT/data/js_$S.rav" &
  $CS rav "$S" "$RAV" "$OUT/data/cs_$S.rav" &
  wait
done
echo "== compare"
node "$HERE/compare.js" "$OUT/data"
