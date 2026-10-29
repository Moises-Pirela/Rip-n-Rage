#!/usr/bin/env bash
# Installs a private .NET 10 SDK into Standalone/.dotnet without root, using Microsoft's official dotnet-install script.
# Only needed when no system-wide `dotnet` (10.0 or newer) is available.
set -euo pipefail
cd "$(dirname "$0")/.."
DEST="$PWD/.dotnet"
if [ -x "$DEST/dotnet" ]; then echo ".NET SDK already present: $("$DEST/dotnet" --version)"; exit 0; fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

curl -fsSL -o "$TMP/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
bash "$TMP/dotnet-install.sh" --channel 10.0 --install-dir "$DEST" --no-path
echo "Installed .NET SDK $("$DEST/dotnet" --version) into $DEST"

if [ "$(uname -s)" = "Linux" ] && [ ! -f native/linux-x64/libglfw.so.3 ] && ! ldconfig -p 2>/dev/null | grep -q libglfw.so.3; then
  echo "Note: GLFW was not found. Install it (e.g. 'sudo apt install libglfw3 libopenal1') or copy"
  echo "      libglfw.so.3 / libopenal.so.1 into native/linux-x64/."
fi
