#!/usr/bin/env bash
# Builds (if needed) and opens the particle effect editor: ./vfx.sh [--effect impact_concrete]
set -e
cd "$(dirname "$0")"
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.VfxEditor/RipNRage.VfxEditor.csproj
exec dotnet src/RipNRage.VfxEditor/bin/Release/net10.0/RipNRageVfxEditor.dll "$@"
