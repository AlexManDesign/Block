#!/bin/sh
# Mesher parity: JS reference mesh worker vs C# MeshCore on identical jobs.
# Usage: sh Tools/parity-mesh/run.sh <pretty.js> [outDir] [--quick] [--only=substr]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PRETTY="$1"; shift || true
[ -n "$PRETTY" ] && [ -f "$PRETTY" ] || { echo "usage: sh run.sh <pretty.js> [outDir] [--quick] [--only=substr]"; exit 2; }
OUT="${TMPDIR:-/tmp}/vf-parity-mesh"
case "$1" in --*|"") ;; *) OUT="$1"; shift ;; esac
rm -rf "$OUT/jobs" "$OUT/js" "$OUT/cs"; mkdir -p "$OUT/build"
echo "== JS: generating jobs and running the reference mesher"
node "$HERE/gen-jobs.js" "$PRETTY" "$OUT" "$@"
echo "== C#: building MeshParity.exe"
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
    -out:"$OUT/build/MeshParity.exe" "$OUT/build/UnityStubs.patched.cs" "$HERE/MeshParity.cs" @"$OUT/build/files.rsp"
echo "== C#: running MeshCore"
VF_RES_DIR="$ROOT/Assets/VoxelForge/Resources" mono "$OUT/build/MeshParity.exe" "$OUT" > "$OUT/cs.log"
echo "== compare"
node "$HERE/compare.js" "$OUT"
