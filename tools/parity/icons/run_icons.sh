#!/usr/bin/env bash
# Inventory icon parity: port MainIconPainter (over the Unity atlas.png) vs main.js Hn() canvases.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:-$here/../../.cache/icons}"; mkdir -p "$out"
(cd "$here/../../refgame" && node run.mjs 6000 "$here/ref_icons.js" > "$out/ref.json" 2> "$out/ref.log")
python3 -c "import json,sys;d=json.load(open(sys.argv[1]));json.dump(d['ids'],open(sys.argv[2],'w'))" "$out/ref.json" "$out/ids.json"
(cd "$here/cs" && dotnet build -c Release -o ../bin -v q -nologo > "$out/build.log")
dotnet "$here/bin/IconParity.dll" "$here/../../../UnityPort/Assets/Resources/Voxel/atlas.png" "$out/ids.json" "$out/port.json"
python3 "$here/compare_icons.py" "$out/ref.json" "$out/port.json" 2
