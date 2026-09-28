#!/usr/bin/env bash
# Compile-check UnityPort scripts without a Unity Editor (needs dotnet SDK 8+ and access to api.nuget.org).
#  - runtime: every Assets/**/*.cs except Editor folders, against UnityEngine.Modules 2021.3.33
#  - editor : everything incl. Editor folders, plus UnityEditor.dll from Unity3D.SDK 2021.1.14.1
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
cache="$here/../.cache"
mkdir -p "$cache"
if [ ! -d "$cache/unityengine.modules/lib/netstandard2.0" ]; then
  curl -sSL -o "$cache/um.nupkg" https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
  mkdir -p "$cache/unityengine.modules" && (cd "$cache/unityengine.modules" && unzip -q -o "$cache/um.nupkg")
fi
if [ ! -f "$cache/unityeditor/UnityEditor.dll" ]; then
  curl -sSL -o "$cache/sdk.nupkg" https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg
  mkdir -p "$cache/unityeditor" && unzip -q -o -j "$cache/sdk.nupkg" lib/UnityEditor.dll -d "$cache/unityeditor"
fi
status=0
for proj in UnityPort.CompileCheck.csproj UnityPort.EditorCheck.csproj; do
  out="$(dotnet build "$here/$proj" -nologo -v q -clp:NoSummary 2>&1)"
  code=$?
  echo "$out" | grep -E "error CS|warning CS" | sed "s#$root/##g; s# \[[^]]*\]\$##" | sort -u
  if [ $code -eq 0 ]; then echo "$proj: OK"; else echo "$proj: FAILED"; status=1; fi
done
exit $status
