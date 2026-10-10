#!/bin/sh
# Voxel light parity: original JS light engine (pretty.js) vs VF.Light.cs / VF.LightRules.cs on identical scenarios.
# Usage: sh Tools/parity-light/run.sh <pretty.js> [outDir] [--rounds=N] [--win=caves|ocean]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PRETTY="$1"; shift || true
[ -n "$PRETTY" ] && [ -f "$PRETTY" ] || { echo "usage: sh run.sh <pretty.js> [outDir] [--rounds=N] [--win=name]"; exit 2; }
OUT="${TMPDIR:-/tmp}/vf-parity-light"
case "$1" in --*|"") ;; *) OUT="$1"; shift ;; esac
WINS="caves ocean pyramid"; EXTRA=""
for a in "$@"; do case "$a" in --win=*) WINS="${a#--win=}" ;; *) EXTRA="$EXTRA $a" ;; esac; done
mkdir -p "$OUT/build"
echo "== C#: building LightParity.exe"
node -e '
const fs=require("fs");let s=fs.readFileSync(process.argv[1],"utf8");
const rep=(a,b)=>{if(!s.includes(a))throw new Error("stub patch anchor not found: "+a);s=s.replace(a,b)};
rep("public class TextAsset : Object { public string text { get { return \"\"; } } public byte[] bytes { get { return null; } } }",
    "public class TextAsset : Object { public string _t; public byte[] _b; public string text { get { return _t ?? \"\"; } } public byte[] bytes { get { return _b; } } }");
rep("public static T Load<T>(string path) where T : Object { return null; }",
    "public static T Load<T>(string path) where T : Object { if (typeof(T) != typeof(TextAsset)) return null; var d = System.Environment.GetEnvironmentVariable(\"VF_RES_DIR\"); foreach (var ext in new[] { \".json\", \".bytes\", \".txt\" }) { var f = System.IO.Path.Combine(d, path + ext); if (System.IO.File.Exists(f)) return (T)(Object)new TextAsset { _t = System.IO.File.ReadAllText(f), _b = System.IO.File.ReadAllBytes(f) }; } return null; }");
fs.writeFileSync(process.argv[2],s);' "$ROOT/Tools/compile-check/UnityStubs.cs" "$OUT/build/UnityStubs.patched.cs"
find "$ROOT/Assets/VoxelForge/Scripts" -name '*.cs' > "$OUT/build/files.rsp"
mcs -langversion:7.2 -target:exe -nowarn:0162,0168,0219,0414,0649,0169,0067,0618 -r:System.Core.dll \
    -out:"$OUT/build/LightParity.exe" "$OUT/build/UnityStubs.patched.cs" "$HERE/LightParity.cs" @"$OUT/build/files.rsp"
FAIL=0
for W in $WINS; do
  echo "== window $W"
  node "$HERE/light-js.js" "$PRETTY" "$OUT" "$W" $EXTRA
  SEED=$(node -e 'const s=require("fs").readFileSync(process.argv[1],"utf8");const m=s.match(new RegExp(process.argv[2]+": \\{ seed: (-?\\d+)"));console.log(m[1])' "$HERE/light-js.js" "$W")
  VF_RES_DIR="$ROOT/Assets/VoxelForge/Resources" mono "$OUT/build/LightParity.exe" "$OUT" "$W" "$SEED"
  if ! node "$HERE/compare.js" "$OUT" "$W" > "$OUT/$W/compare.txt"; then
    cat "$OUT/$W/compare.txt"; FAIL=1
    STEP=$(sed -n 's/.*FIRST_STEP=\([0-9]*\).*/\1/p' "$OUT/$W/compare.txt")
    echo "== voxel diff at step $STEP"
    node "$HERE/light-js.js" "$PRETTY" "$OUT" "$W" $EXTRA --dump="$STEP" > /dev/null
    VF_RES_DIR="$ROOT/Assets/VoxelForge/Resources" mono "$OUT/build/LightParity.exe" "$OUT" "$W" "$SEED" --dump="$STEP" > /dev/null
    node "$HERE/compare.js" --dump "$OUT" "$W" "$STEP" || true
  else cat "$OUT/$W/compare.txt"; fi
done
node "$HERE/compare.js" --rules "$OUT" || FAIL=1
[ "$FAIL" = 0 ] && echo "light parity: OK" || { echo "light parity: FAILED"; exit 1; }
