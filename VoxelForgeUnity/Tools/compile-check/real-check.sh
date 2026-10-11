#!/bin/sh
# Real compile check: the Roslyn compiler (C# 9, as Unity 2022.3 uses) against the real UnityEngine
# module assemblies (and UnityEditor for the Editor folder), all fetched from NuGet (UnityEngine.Modules 2021.3.33: same API surface the port
# uses as 2022.3). Needs mono (with mono-devel facades), curl and unzip.
# Usage: sh Tools/compile-check/real-check.sh        (from the VoxelForgeUnity folder or anywhere)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJ="$(cd "$HERE/../.." && pwd)"
CACHE="${VF_REAL_CHECK_CACHE:-${TMPDIR:-/tmp}/vf-real-check}"
NUGET=https://api.nuget.org/v3-flatcontainer
mkdir -p "$CACHE"
if [ ! -f "$CACHE/ue/lib/net45/UnityEngine.CoreModule.dll" ]; then
  curl -sSfL -o "$CACHE/ue.nupkg" "$NUGET/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg"
  mkdir -p "$CACHE/ue" && (cd "$CACHE/ue" && unzip -q -o ../ue.nupkg)
fi
if [ ! -f "$CACHE/sdk/lib/UnityEditor.dll" ]; then
  curl -sSfL -o "$CACHE/sdk.nupkg" "$NUGET/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg"
  mkdir -p "$CACHE/sdk" && (cd "$CACHE/sdk" && unzip -q -o ../sdk.nupkg)
fi
if [ ! -f "$CACHE/roslyn/tasks/net472/csc.exe" ]; then
  curl -sSfL -o "$CACHE/roslyn.nupkg" "$NUGET/microsoft.net.compilers.toolset/4.3.1/microsoft.net.compilers.toolset.4.3.1.nupkg"
  mkdir -p "$CACHE/roslyn" && (cd "$CACHE/roslyn" && unzip -q -o ../roslyn.nupkg)
fi
M=/usr/lib/mono/4.7.2-api; [ -d "$M" ] || M=/usr/lib/mono/4.5
[ -f "$M/Facades/netstandard.dll" ] || { echo "missing $M/Facades/netstandard.dll (install mono-devel)"; exit 2; }
REFS=""; for f in "$CACHE"/ue/lib/net45/*.dll; do REFS="$REFS -r:$f"; done
find "$PROJ/Assets/VoxelForge/Scripts" -name '*.cs' > "$CACHE/files.rsp"
mono "$CACHE/roslyn/tasks/net472/csc.exe" -nologo -noconfig -langversion:9.0 -target:library -nostdlib \
  -r:"$M/mscorlib.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" -r:"$M/Facades/netstandard.dll" $REFS \
  -nowarn:1701,1702 -out:"$CACHE/Assembly-CSharp.dll" @"$CACHE/files.rsp"
echo "runtime scripts (Roslyn C# 9 + UnityEngine 2021.3 modules): OK"
mono "$CACHE/roslyn/tasks/net472/csc.exe" -nologo -noconfig -langversion:9.0 -target:library -nostdlib \
  -r:"$M/mscorlib.dll" -r:"$M/System.dll" -r:"$M/System.Core.dll" -r:"$M/Facades/netstandard.dll" $REFS \
  -r:"$CACHE/sdk/lib/UnityEditor.dll" -r:"$CACHE/Assembly-CSharp.dll" -out:"$CACHE/VoxelForge.Editor.dll" \
  $(find "$PROJ/Assets/VoxelForge/Editor" -name '*.cs')
echo "editor scripts (+ UnityEditor 2021.1): OK"
