#!/bin/sh
#
# One command from the working tree to the file you send someone.
#
#     tools/ship.sh                 # Windows (the default)
#     tools/ship.sh "macOS"
#     tools/ship.sh "Linux"
#
# It refuses to build anything if a harness fails. A release nobody can
# reproduce is a release nobody can bisect, so the gate is part of the build
# rather than a thing you are supposed to remember.
#
# Requires the .NET editor build and MATCHING export templates (the mono ones,
# 4.6.2.stable.mono). Missing templates fail the export with "No export
# template found"; install them once from the editor, Editor > Manage Export
# Templates > Download and Install.

set -e

GODOT="${GODOT:-/Applications/Godot-4.6-dotnet.app/Contents/MacOS/Godot}"
PRESET="${1:-Windows Desktop}"

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT=$(cd "$ROOT/.." && pwd)/build
cd "$ROOT"

VERSION=$(sed -n 's/^config\/version="\(.*\)"$/\1/p' project.godot)
[ -n "$VERSION" ] || { echo "no config/version in project.godot"; exit 1; }

case "$PRESET" in
	"Windows Desktop") DIR="$OUT/windows"; FILE="Cognitohazard.exe";    TAG="windows" ;;
	"macOS")           DIR="$OUT/macos";   FILE="Cognitohazard.app";    TAG="macos"   ;;
	"Linux")           DIR="$OUT/linux";   FILE="Cognitohazard.x86_64"; TAG="linux"   ;;
	*) echo "unknown preset: $PRESET"; exit 1 ;;
esac

LOG=$(mktemp /tmp/cogship.XXXXXX)
trap 'rm -f "$LOG"' EXIT

# A step, its exit status taken from the STEP and not from whatever formats its
# output. `cmd | tail -1` reports tail's status, which is always 0 -- so a
# harness piped into tail is a gate that passes every time.
step() {
	label=$1
	shift
	printf '  %-16s ' "$label"
	if "$@" > "$LOG" 2>&1; then
		summary=$(grep -E 'passed|Build succeeded' "$LOG" | tail -1 | sed 's/^[[:space:]]*//')
		echo "${summary:-ok}"
	else
		echo "FAILED"
		echo
		tail -25 "$LOG"
		exit 1
	fi
}

echo "== Cognitohazard $VERSION -> $PRESET =="

step "C# build" dotnet build -v quiet
step "sim" dotnet run --project tests/Cognitohazard.Tests.csproj
for t in editor_check inventory_check fuzz_check audio_check level_art_check; do
	step "$t" "$GODOT" --headless --path . --script "res://tests/$t.gd"
done
step "import" "$GODOT" --headless --path . --import

mkdir -p "$DIR"
cp tools/player_readme.txt "$DIR/README.txt"
step "export" "$GODOT" --headless --path . --export-release "$PRESET" "$DIR/$FILE"

ZIP="Cognitohazard-$VERSION-$TAG.zip"
rm -f "$DIR/$ZIP"
# -r because the macOS build is a directory; harmless on the single files.
(cd "$DIR" && zip -9 -q -r "$ZIP" "$FILE" README.txt)

echo
echo "built:  $DIR/$FILE"
echo "send:   $DIR/$ZIP  ($(ls -lh "$DIR/$ZIP" | awk '{print $5}'))"
