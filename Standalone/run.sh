#!/usr/bin/env bash
# Builds (if needed) and runs the standalone game using the private .NET toolchain in ./.dotnet.
# No root, no internet needed once .dotnet exists; run tools/setup-dotnet.sh otherwise (Ubuntu archive only).
set -e
cd "$(dirname "$0")"
[ -x .dotnet/dotnet ] || command -v dotnet >/dev/null || tools/setup-dotnet.sh
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.Game/RipNRage.Game.csproj
exec dotnet src/RipNRage.Game/bin/Release/net10.0/RipNRage.dll "$@"
