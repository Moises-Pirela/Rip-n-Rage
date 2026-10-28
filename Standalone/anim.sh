#!/usr/bin/env bash
# Builds (if needed) and opens the sprite animation editor: ./anim.sh [--clip GlockFire | --controller LeftFist]
set -e
cd "$(dirname "$0")"
[ -x .dotnet/dotnet ] || command -v dotnet >/dev/null || tools/setup-dotnet.sh
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.AnimEditor/RipNRage.AnimEditor.csproj
exec dotnet src/RipNRage.AnimEditor/bin/Release/net8.0/RipNRageAnimEditor.dll "$@"
