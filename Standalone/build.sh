#!/usr/bin/env bash
# Builds the solution and publishes a self-contained Linux x64 binary into dist/linux-x64 (no SDK/runtime needed to run it).
set -e
cd "$(dirname "$0")"
[ -x .dotnet/dotnet ] || command -v dotnet >/dev/null || tools/setup-dotnet.sh
source tools/env.sh
dotnet build -c Release --nologo -v q RipNRage.sln
dotnet publish -c Release --nologo -v q -r linux-x64 --self-contained true -o dist/linux-x64 src/RipNRage.Game/RipNRage.Game.csproj
dotnet publish -c Release --nologo -v q -r linux-x64 --self-contained true -o dist/linux-x64 src/RipNRage.Editor/RipNRage.Editor.csproj
dotnet publish -c Release --nologo -v q -r linux-x64 --self-contained true -o dist/linux-x64 src/RipNRage.AnimEditor/RipNRage.AnimEditor.csproj
echo "Self-contained build: dist/linux-x64/RipNRage (game), RipNRageEditor (level editor), RipNRageAnimEditor (animation editor)"
