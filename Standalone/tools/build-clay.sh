#!/usr/bin/env bash
# Compiles the vendored Clay (native/src/clay) into native/<rid>/ for this machine.
#   tools/build-clay.sh            macOS: osx-arm64 + osx-x64; Linux: linux-x64 (or the host arch)
# Windows: run tools/build-clay.cmd from a Visual Studio developer prompt (or let CI build it).
set -euo pipefail
cd "$(dirname "$0")/.."
SRC=native/src/clay/clay.c
FLAGS="-std=c99 -O2 -shared -fPIC -Wno-unused-function"
case "$(uname -s)" in
  Darwin)
    for a in arm64:osx-arm64 x86_64:osx-x64; do
      arch=${a%%:*}; rid=${a##*:}; mkdir -p native/$rid
      clang $FLAGS -arch $arch -mmacosx-version-min=11.0 -install_name @rpath/libclay.dylib -o native/$rid/libclay.dylib $SRC
      echo "built native/$rid/libclay.dylib"
    done ;;
  *)
    case "$(uname -m)" in aarch64|arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac
    mkdir -p native/$rid
    ${CC:-cc} $FLAGS -o native/$rid/libclay.so $SRC
    echo "built native/$rid/libclay.so" ;;
esac
