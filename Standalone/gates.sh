#!/usr/bin/env bash
# The gate sweep: builds the game AND the test projects (a solution build does not refresh GameChecks' copy of the
# game assembly — the stale-binary trap of layer 7), runs both check suites, then every gate script under the gate
# rule (--fixed-dt 0.016667 --threads 1 --validate-access, default seed). Writes one log per script plus its probe
# lines (`[test]` and anything that threw) to OUT, and exits non-zero if any suite or script failed.
#
#   ./gates.sh [OUT]                 sweep into OUT (default: gates-out)
#   ./gates.sh --compare A B         diff the probe lines of two sweeps (byte-identical = no output)
#   GATES="die fall" ./gates.sh      run only the named scripts (checks still run)
set -u
cd "$(dirname "$0")"
source tools/env.sh

if [ "${1:-}" = "--compare" ]; then
    status=0
    for f in "$2"/*.test; do
        n=$(basename "$f")
        if ! diff -q "$f" "$3/$n" >/dev/null 2>&1; then echo "== $n differs"; diff "$f" "$3/$n" | head -20; status=1; fi
    done
    exit $status
fi

OUT="${1:-gates-out}"
mkdir -p "$OUT"
fail=0

dotnet build -c Release --nologo -v q src/RipNRage.Game/RipNRage.Game.csproj || exit 1
dotnet build -c Release --nologo -v q tests/RipNRage.EngineChecks || exit 1
dotnet build -c Release --nologo -v q tests/RipNRage.GameChecks || exit 1

dotnet run -c Release --no-build --project tests/RipNRage.EngineChecks > "$OUT/engine-checks.log" 2>&1 || fail=1
tail -1 "$OUT/engine-checks.log"
dotnet run -c Release --no-build --project tests/RipNRage.GameChecks > "$OUT/game-checks.log" 2>&1 || fail=1
tail -1 "$OUT/game-checks.log"

# script:frames — the 20 of the 7.8 sweep, then fall / pause (in the sweep since 8.0) and checkpoint (new in 8.0)
ALL="walk:400 fire:400 weapons:400 rage:400 saveload:400 jab:400 m1887:400 glockfire:400 rocket:400 glockalt:400
killenemy:400 style:400 die:400 peek:400 close:400 approach:400 pickup:400 water:400 surface:400 evade:400
fall:620 pause:400 checkpoint:400 menu:400"

for entry in $ALL; do
    name=${entry%%:*}; frames=${entry##*:}
    if [ -n "${GATES:-}" ] && [[ " $GATES " != *" $name "* ]]; then continue; fi
    log="$OUT/$name.log"
    extra=""; [ "$name" = menu ] && extra="--menu" # the title-screen gate starts on the title screen (8.8)
    dotnet src/RipNRage.Game/bin/Release/net10.0/RipNRage.dll $extra --script "$name" --frames "$frames" \
        --fixed-dt 0.016667 --threads 1 --validate-access --no-vsync --width 640 --height 360 --overlay 0 \
        > "$log" 2>&1
    code=$?
    grep -E '\[test\]|Exception|Fatal' "$log" > "$OUT/$name.test"
    if [ $code -ne 0 ]; then echo "FAIL $name (exit $code)"; fail=1; else echo "ok   $name"; fi
done

# pixel gates (8.0; same-build floor 0.000%): the HUD on `walk`, the death camera mid-fall on `die`, both at frame 120.
# Compare two sweeps with: python3 tools/pngdiff.py A/shot-walk.png B/shot-walk.png
for name in walk die; do
    if [ -n "${GATES:-}" ] && [[ " $GATES " != *" $name "* ]]; then continue; fi
    dotnet src/RipNRage.Game/bin/Release/net10.0/RipNRage.dll --script "$name" --frames 120 \
        --fixed-dt 0.016667 --threads 1 --no-vsync --width 640 --height 360 --overlay 0 \
        --screenshot "$OUT/shot-$name.png" > /dev/null 2>&1 || { echo "FAIL shot-$name"; fail=1; }
done
exit $fail
