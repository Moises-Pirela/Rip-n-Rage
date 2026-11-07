#!/usr/bin/env bash
# Builds the solution and publishes self-contained binaries (no SDK / runtime needed to run them).
#   ./build.sh               for this machine (linux-x64, osx-arm64 or osx-x64)
#   ./build.sh osx-x64       for another runtime identifier (linux-x64, osx-arm64, osx-x64, win-x64)
# On macOS targets the output also gets a double-clickable dist/<rid>/Rip-n-Rage.app (Retina-enabled, ad-hoc signed).
set -e
cd "$(dirname "$0")"
source tools/env.sh

RID="$1"
if [ -z "$RID" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) RID=osx-arm64 ;;
    Darwin-*)     RID=osx-x64 ;;
    *)            RID=linux-x64 ;;
  esac
fi
OUT="dist/$RID"

dotnet build -c Release --nologo -v q RipNRage.sln
for p in Game Editor AnimEditor VfxEditor SkeletonEditor Benchmark; do
  dotnet publish -c Release --nologo -v q -r "$RID" --self-contained true -o "$OUT" "src/RipNRage.$p/RipNRage.$p.csproj"
done
echo "Self-contained build: $OUT/RipNRage (game), RipNRageEditor (level editor), RipNRageAnimEditor (animation editor), RipNRageVfxEditor (effect editor), RipNRageSkeletonEditor (sprite destruction skeleton editor), RipNRageBenchmark (physics / engine benchmark)"

case "$RID" in osx-*) tools/make-macos-app.sh "$OUT" ;; esac
