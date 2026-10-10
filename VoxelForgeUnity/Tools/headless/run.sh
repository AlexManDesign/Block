#!/bin/sh
# Headless runtime smoke test: builds the game scripts + HeadlessUnity.cs + Harness.cs with Mono mcs and runs the scripted session.
# Usage: sh Tools/headless/run.sh [harness options: --verbose --frame-ms N --scale K --data DIR --only a,b]
# VF_TRACE_REV=1: build an instrumented copy of the scripts that records who bumps chunk.meshRev (see TraceRev.cs).
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$(cd "$HERE/../.." && pwd)"
OUT="${TMPDIR:-/tmp}/vf-headless"
mkdir -p "$OUT"
SRC="$PROJ/Assets/VoxelForge/Scripts"
EXTRA=""
if [ -n "$VF_TRACE_REV" ]; then
  rm -rf "$OUT/src"; cp -r "$SRC" "$OUT/src"; SRC="$OUT/src"
  find "$SRC" -name '*.cs' -exec sed -i 's/\bc\.meshRev++/VF.__traceRev(c)/g' {} +
  sed -i 's/^\( *\)fluidMutationDepth++;/\1fluidMutationDepth++; VF.__traceFluid(x, y, z, id);/' "$SRC/World/VF.World.cs"
  EXTRA="-define:VF_TRACE_REV $HERE/TraceRev.cs"
fi
find "$SRC" -name '*.cs' > "$OUT/files.rsp"
mcs -langversion:7.2 -debug -unsafe -nowarn:0162,0168,0219,0414,0649,0169,0067,0618,0660,0661 \
    -r:System.Core.dll -out:"$OUT/vf-headless.exe" $EXTRA "$HERE/HeadlessUnity.cs" "$HERE/Harness.cs" @"$OUT/files.rsp"
echo "build: OK"
exec mono --debug "$OUT/vf-headless.exe" --project "$PROJ" "$@"
