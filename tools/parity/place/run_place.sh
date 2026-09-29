#!/usr/bin/env bash
# Placement / use / support parity: reference main.js (headless) vs the Unity port rules.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:-$here/../../.cache/place}"; mkdir -p "$out"
node "$here/gen_scenario.mjs" "$out/scenario.json"
{ printf 'var __SCENARIO__='; cat "$out/scenario.json"; printf ';\n'; cat "$here/ref_place.js"; } > "$out/ref_eval.js"
(cd "$here/../../refgame" && node run.mjs 6000 "$out/ref_eval.js" > "$out/ref.json" 2> "$out/ref.log")
(cd "$here/cs" && dotnet build -c Release -o ../bin -v q -nologo > "$out/build.log")
dotnet "$here/bin/PlaceParity.dll" "$out/scenario.json" "$out/port.json"
python3 "$here/compare_place.py" "$out/ref.json" "$out/port.json"
