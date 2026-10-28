#!/usr/bin/env bash
# Installs a private .NET 8 SDK into Standalone/.dotnet WITHOUT root and WITHOUT Microsoft's servers:
# the packages are fetched from the Ubuntu 24.04 archive (archive.ubuntu.com) and unpacked with dpkg-deb.
# Also fetches GLFW / OpenAL if the bundled native libs are missing.
set -euo pipefail
cd "$(dirname "$0")/.."
DEST="$PWD/.dotnet"
if [ -x "$DEST/dotnet" ]; then echo ".NET SDK already present: $("$DEST/dotnet" --version)"; exit 0; fi

MIRROR="${UBUNTU_MIRROR:-https://archive.ubuntu.com/ubuntu}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Resolve current package file names from the archive index (versions move with security updates).
echo "Reading package index from $MIRROR ..."
curl -fsSL "$MIRROR/dists/noble-updates/main/binary-amd64/Packages.gz" | gzip -dc > "$TMP/Packages"
curl -fsSL "$MIRROR/dists/noble/main/binary-amd64/Packages.gz" | gzip -dc >> "$TMP/Packages"
curl -fsSL "$MIRROR/dists/noble/universe/binary-amd64/Packages.gz" | gzip -dc >> "$TMP/Packages"

find_pkg() { # prints the Filename of the newest entry for a package name
  awk -v pkg="$1" 'BEGIN{RS="";FS="\n"} { name=""; file=""; ver="";
    for(i=1;i<=NF;i++){ if($i ~ /^Package: /) name=substr($i,10); if($i ~ /^Filename: /) file=substr($i,11); if($i ~ /^Version: /) ver=substr($i,10); }
    if(name==pkg) print ver "\t" file }' "$TMP/Packages" | sort -V | tail -1 | cut -f2
}

PKGS="dotnet-sdk-8.0 dotnet-runtime-8.0 dotnet-host-8.0 dotnet-hostfxr-8.0 dotnet-targeting-pack-8.0 dotnet-apphost-pack-8.0 netstandard-targeting-pack-2.1-8.0 aspnetcore-runtime-8.0 aspnetcore-targeting-pack-8.0 dotnet-templates-8.0"
[ -f native/linux-x64/libglfw.so.3 ] || PKGS="$PKGS libglfw3"
[ -f native/linux-x64/libopenal.so.1 ] || PKGS="$PKGS libopenal1"

mkdir -p "$TMP/root"
for p in $PKGS; do
  f="$(find_pkg "$p")"
  [ -n "$f" ] || { echo "package $p not found in index"; exit 1; }
  echo "fetching $p ($(basename "$f"))"
  curl -fsSL -o "$TMP/pkg.deb" "$MIRROR/$f"
  dpkg-deb -x "$TMP/pkg.deb" "$TMP/root"
done

mkdir -p "$DEST"
cp -a "$TMP/root/usr/lib/dotnet/." "$DEST/"
mkdir -p native/linux-x64
[ -f native/linux-x64/libglfw.so.3 ] || cp "$TMP"/root/usr/lib/x86_64-linux-gnu/libglfw.so.3.* native/linux-x64/libglfw.so.3
[ -f native/linux-x64/libopenal.so.1 ] || cp "$TMP"/root/usr/lib/x86_64-linux-gnu/libopenal.so.1.* native/linux-x64/libopenal.so.1
echo "Installed .NET SDK $("$DEST/dotnet" --version) into $DEST"
