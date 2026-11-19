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
    for f in "$2"/*.test "$2"/*.level; do
        [ -e "$f" ] || continue
        n=$(basename "$f")
        if ! diff -q "$f" "$3/$n" >/dev/null 2>&1; then echo "== $n differs"; diff "$f" "$3/$n" | head -20; status=1; fi
    done
    exit $status
fi

# --batch k/n runs only the k-th slice of the script list, so a sweep fits in four short runs instead of one long
# one (11.10d: the full sweep kept being cut off). --list prints the slice and runs nothing.
BATCH=""; LIST=0
while [ $# -gt 0 ]; do
    case "$1" in
        --batch) BATCH="$2"; shift 2 ;;
        --list) LIST=1; shift ;;
        *) break ;;
    esac
done

SLICE_PENDING=1
OUT="${1:-gates-out}"
mkdir -p "$OUT"
# hermetic: every sweep gets its own saves / graphics.json / controls.json (other worktrees run the game too, and
# share the per-user folder — a checkpoint written by one was read by another)
export RIPNRAGE_DATA_DIR="$(cd "$OUT" && pwd)/data"
rm -rf "$RIPNRAGE_DATA_DIR"; mkdir -p "$RIPNRAGE_DATA_DIR"
fail=0

# script:frames — the 20 of the 7.8 sweep, then fall / pause (in the sweep since 8.0) and checkpoint (new in 8.0)
ALL="maze:400 door:400 ledge:400 stay:600 crowd:600 brawler:400 walk:400 fire:400 weapons:400 rage:400 saveload:400 jab:400 m1887:400 glockfire:400 rocket:400 glockalt:400
killenemy:400 style:400 die:400 peek:400 close:400 approach:400 pickup:400 water:400 surface:400 evade:400
fall:620 pause:400 checkpoint:400 menu:400 flowmoves:400 deathf8:400"

# the slice: entries k, k+n, k+2n ... so every batch mixes cheap and dear scripts
SLICE="$ALL"
if [ -n "$BATCH" ]; then
    k=${BATCH%%/*}; n=${BATCH##*/}
    SLICE=""; i=0
    for entry in $ALL; do
        if [ $(( i % n + 1 )) -eq "$k" ]; then SLICE="$SLICE $entry"; fi
        i=$(( i + 1 ))
    done
fi

if [ "$LIST" = 1 ]; then echo $SLICE | tr ' ' '\n' | sed '/^$/d'; exit 0; fi
[ -n "$BATCH" ] && echo "batch $BATCH: $(echo $SLICE | tr '\n' ' ')"

dotnet build -c Release --nologo -v q src/RipNRage.Game/RipNRage.Game.csproj || exit 1
dotnet build -c Release --nologo -v q tests/RipNRage.EngineChecks || exit 1
dotnet build -c Release --nologo -v q tests/RipNRage.GameChecks || exit 1

dotnet run -c Release --no-build --project tests/RipNRage.EngineChecks > "$OUT/engine-checks.log" 2>&1 || fail=1
tail -1 "$OUT/engine-checks.log"
dotnet run -c Release --no-build --project tests/RipNRage.GameChecks > "$OUT/game-checks.log" 2>&1 || fail=1
tail -1 "$OUT/game-checks.log"

for entry in $SLICE; do
    name=${entry%%:*}; frames=${entry##*:}
    if [ -n "${GATES:-}" ] && [[ " $GATES " != *" $name "* ]]; then continue; fi
    log="$OUT/$name.log"
    extra=""; [ "$name" = menu ] && extra="--menu" # the title-screen gate starts on the title screen (8.8)
    # 10.0: the level manifest — what the scene built, entity by entity (the gate for moving a class onto a prefab)
    case "$name" in walk|menu) extra="$extra --dump-level $OUT/$name.level" ;; esac
    dotnet src/RipNRage.Game/bin/Release/net10.0/RipNRage.dll $extra --script "$name" --frames "$frames" \
        --fixed-dt 0.016667 --threads 1 --validate-access --no-vsync --width 640 --height 360 --overlay 0 \
        > "$log" 2>&1
    code=$?
    grep -E '\[test\]|Exception|Fatal' "$log" > "$OUT/$name.test"
    if [ $code -ne 0 ]; then echo "FAIL $name (exit $code)"; fail=1; else echo "ok   $name"; fi
done

# pixel gates (8.0 / 9.0; same-build floor 0.000%): the HUD on `walk` and the death camera mid-fall on `die` at
# frame 120; 9.0 adds the level from the start (`look`, frame 60) and the title over it (`menu`, frame 50) — layer 9
# moves everything visible onto entities, and these are the proof.
# Compare two sweeps with: python3 tools/pngdiff.py A/shot-walk.png B/shot-walk.png
for shot in walk:120 die:120 look:60 menu:50; do
    name=${shot%%:*}; frames=${shot##*:}
    if [ -n "${GATES:-}" ] && [[ " $GATES " != *" $name "* ]]; then continue; fi
    extra=""; [ "$name" = menu ] && extra="--menu"
    dotnet src/RipNRage.Game/bin/Release/net10.0/RipNRage.dll $extra --script "$name" --frames "$frames" \
        --fixed-dt 0.016667 --threads 1 --no-vsync --width 640 --height 360 --overlay 0 \
        --screenshot "$OUT/shot-$name.png" > /dev/null 2>&1 || { echo "FAIL shot-$name"; fail=1; }
done

# the level editor (10.0): the tools are gates too — 10.6 edits entity properties through its panels. The script
# saves, so it runs on a copy of the level inside OUT: a sweep never writes the repo's dock.map.
if [ -z "${GATES:-}" ] || [[ " $GATES " == *" edit "* ]]; then
    cp levels/dock.map "$OUT/dock.map"
    dotnet build -c Release --nologo -v q src/RipNRage.Editor/RipNRage.Editor.csproj > "$OUT/edit-build.log" 2>&1 || fail=1
    dotnet src/RipNRage.Editor/bin/Release/net10.0/RipNRageEditor.dll "$OUT/dock.map" --script session --frames 90 \
        --fixed-dt 0.016667 --width 640 --height 360 --screenshot "$OUT/shot-edit.png" > "$OUT/edit.log" 2>&1
    code=$?
    grep -E '\[test\]|\[Error\] level|Exception|Fatal' "$OUT/edit.log" > "$OUT/edit.test" # a level error in the preview is a failure of the editor
    if [ $code -ne 0 ]; then echo "FAIL edit (exit $code)"; fail=1; else echo "ok   edit"; fi

    # 11.17: what the editor knows about navigation - the grid, the collision it can bake from, the broadphase
    dotnet src/RipNRage.Editor/bin/Release/net10.0/RipNRageEditor.dll "$OUT/dock.map" --script nav --frames 40 \
        --fixed-dt 0.016667 --width 640 --height 360 > "$OUT/editnav.log" 2>&1
    code=$?
    grep -E '\[test\]|\[Error\] level|Exception|Fatal' "$OUT/editnav.log" > "$OUT/editnav.test"
    if [ $code -ne 0 ]; then echo "FAIL editnav (exit $code)"; fail=1; else echo "ok   editnav"; fi
    # 10.6: the Entity window's model — sets, reverts, swaps, copies, adds a component, saves a prefab from an
    # instance — then saves the map and reads it back. The prefabs are copied too: "save as prefab" writes a file.
    rm -rf "$OUT/prefabs"; cp -r prefabs "$OUT/prefabs"
    RIPNRAGE_PREFABS="$OUT/prefabs" dotnet src/RipNRage.Editor/bin/Release/net10.0/RipNRageEditor.dll "$OUT/dock.map" --script properties --frames 60 \
        --fixed-dt 0.016667 --width 640 --height 360 --screenshot "$OUT/shot-props.png" > "$OUT/props.log" 2>&1
    code=$?
    grep -E '\[test\]|\[Error\] level|Exception|Fatal' "$OUT/props.log" > "$OUT/props.test"
    if [ $code -ne 0 ]; then echo "FAIL properties (exit $code)"; fail=1; else echo "ok   properties"; fi
fi
exit $fail
