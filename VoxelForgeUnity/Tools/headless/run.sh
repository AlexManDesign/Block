#!/bin/sh
# Headless runtime smoke test: builds the game scripts + HeadlessUnity.cs + Harness.cs with Mono mcs and runs the scripted session.
# Usage: sh Tools/headless/run.sh [harness options: --verbose --frame-ms N --scale K --data DIR]
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$(cd "$HERE/../.." && pwd)"
OUT="${TMPDIR:-/tmp}/vf-headless"
mkdir -p "$OUT"
find "$PROJ/Assets/VoxelForge/Scripts" -name '*.cs' > "$OUT/files.rsp"
mcs -langversion:7.2 -debug -unsafe -nowarn:0162,0168,0219,0414,0649,0169,0067,0618,0660,0661 \
    -r:System.Core.dll -out:"$OUT/vf-headless.exe" "$HERE/HeadlessUnity.cs" "$HERE/Harness.cs" @"$OUT/files.rsp"
echo "build: OK"
exec mono --debug "$OUT/vf-headless.exe" --project "$PROJ" "$@"
