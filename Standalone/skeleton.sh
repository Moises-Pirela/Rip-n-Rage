#!/usr/bin/env bash
# Builds (if needed) and opens the sprite destruction skeleton editor: ./skeleton.sh [--skeleton rifleman]
set -e
cd "$(dirname "$0")"
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.SkeletonEditor/RipNRage.SkeletonEditor.csproj
exec dotnet src/RipNRage.SkeletonEditor/bin/Release/net10.0/RipNRageSkeletonEditor.dll "$@"
