#!/usr/bin/env bash
# Builds (if needed) and runs the standalone game using the private .NET toolchain in ./.dotnet.
# Uses any .NET 10 SDK it finds (see tools/env.sh); installs a private one into ./.dotnet if there is none.
set -e
cd "$(dirname "$0")"
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.Game/RipNRage.Game.csproj
exec dotnet src/RipNRage.Game/bin/Release/net10.0/RipNRage.dll "$@"
