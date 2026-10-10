#!/bin/sh
# Offline compile check of the runtime scripts against UnityStubs.cs (Mono mcs, C# 7.2).
# Usage: sh Tools/compile-check/check.sh   (from the VoxelForgeUnity folder)
set -e
cd "$(dirname "$0")/../.."
OUT="${TMPDIR:-/tmp}/vf-compile-check.dll"
find Assets/VoxelForge/Scripts -name '*.cs' > "${OUT}.rsp"
mcs -langversion:7.2 -target:library -nowarn:0162,0168,0219,0414,0649,0169,0067,0618 \
    -r:System.Core.dll -out:"$OUT" Tools/compile-check/UnityStubs.cs @"${OUT}.rsp"
echo "compile check: OK"
