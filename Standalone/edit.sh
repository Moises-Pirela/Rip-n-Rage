#!/usr/bin/env bash
# Builds (if needed) and opens the level editor: ./edit.sh [levels/file.map]
set -e
cd "$(dirname "$0")"
[ -x .dotnet/dotnet ] || command -v dotnet >/dev/null || tools/setup-dotnet.sh
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.Editor/RipNRage.Editor.csproj
exec dotnet src/RipNRage.Editor/bin/Release/net8.0/RipNRageEditor.dll "$@"
