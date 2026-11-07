#!/usr/bin/env bash
# Builds (if needed) and opens the level editor: ./edit.sh [levels/file.map]
set -e
cd "$(dirname "$0")"
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.Editor/RipNRage.Editor.csproj
exec dotnet src/RipNRage.Editor/bin/Release/net10.0/RipNRageEditor.dll "$@"
