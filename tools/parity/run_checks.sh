#!/usr/bin/env bash
# Parity + regression checks for the Unity port without Unity.
#   1. golden fingerprint of gen -> light stitch -> mesh (optimisations must not change output)
#   2. worldgen parity vs reference genWorker.js on 24 widely spread chunks
#   3. mesher parity vs reference meshWorker.js
#   4. block placement / use / support rules vs main.js kT()/gT()/nA() (headless reference game)
# Needs: dotnet 8, node 18+, python3 + numpy.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:-$here/../.cache/parity-out}"
mkdir -p "$out"
dotnet build "$here/cs" -nologo -v q -c Release >/dev/null
bin="dotnet $here/cs/bin/Release/net8.0/Parity.dll"
echo "== golden fingerprint"
: > "$out/golden.txt"
for p in "-67 177" "0 0" "300 -40"; do $bin golden 56 $p >> "$out/golden.txt"; done
if diff -q "$here/golden_expected.txt" "$out/golden.txt" >/dev/null; then echo "golden: IDENTICAL"; else echo "golden: CHANGED"; diff "$here/golden_expected.txt" "$out/golden.txt" || true; fi
echo "== worldgen parity (seed 56)"
C=""; for i in $(seq 0 23); do x=$(( (i*137) % 1200 - 600 )); z=$(( (i*211) % 1400 - 700 )); C="$C $x,$z"; done
rm -rf "$out/gen" && mkdir -p "$out/gen"
node "$here/jsdump.mjs" 56 0 "$out/gen" $C
$bin gen 56 "$out/gen" $C
python3 "$here/compare_gen.py" "$out/gen" | grep -E "TOTAL|->" | head -12
echo "== mesher parity"
rm -rf "$out/mesh" && mkdir -p "$out/mesh"
node "$here/jsmeshdump.mjs" 56 "$out/mesh" -67,-178 0,0
$bin meshparity "$out/mesh" -67,-178 0,0
python3 "$here/compare_mesh.py" "$out/mesh" 0
echo "== pipeline timing (.NET 8)"
$bin pipeline 56 3
echo "== mesher parity: every block x valid metadata (isolated) and 3 dense random worlds"
for mode in "2" "1 1" "1 2" "1 3"; do
  d="$out/synth_${mode// /_}"; rm -rf "$d"
  node "$here/jssynth.mjs" "$d" $mode >/dev/null
  $bin meshparity "$d" 0,0
  python3 "$here/compare_mesh.py" "$d" 0 | tail -1
done
echo "== placement / use / support parity (main.js kT/gT/nA in headless Chromium)"
"$here/place/run_place.sh" "$out/place" | tail -1
