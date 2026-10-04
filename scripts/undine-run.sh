#!/usr/bin/env bash
# Sync this checkout to a remote Linux desktop (undine), build, (re)launch Dogeometric on undine's desktop and fetch a window screenshot.
# Usage: DOGEOMETRIC_REMOTE=user@host [DOGEOMETRIC_SSH_PORT=22] scripts/undine-run.sh [output.png] [seconds-to-wait] [file to open on undine]
set -euo pipefail
HOST="${DOGEOMETRIC_REMOTE:?set DOGEOMETRIC_REMOTE=user@host}"
PORT="${DOGEOMETRIC_SSH_PORT:-22}"
SSH=(ssh -p "$PORT" "$HOST")
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-/tmp/dogeometric-shot.png}"
WAIT="${2:-8}"
OPEN="${3:-}"

rsync -a --delete --exclude .git --exclude bin --exclude obj --exclude .godot --exclude sketchup.tar.zst --exclude logs \
  -e "ssh -p $PORT" "$ROOT/" "$HOST:projects/dogeometric/"

"${SSH[@]}" bash -s "$WAIT" "$(printf %q "$OPEN")" <<'REMOTE'
set -e
cd ~/projects/dogeometric
export DISPLAY=:0 XAUTHORITY=$HOME/.Xauthority
mkdir -p logs
if ! dotnet build app/Dogeometric.csproj -nologo -v q 2>&1 | grep -E "error|Build succeeded" | sort -u; then exit 1; fi
dotnet build app/Dogeometric.csproj -nologo -v q 2>&1 | grep -q "error" && exit 1
pkill -f "[g]odot.x86_64 --path app" || true
~/Godot/godot.x86_64 --headless --path app --import >/dev/null 2>&1 || true
sleep 0.5
nohup ~/Godot/godot.x86_64 --path app -- ${2:+"$2"} > logs/app-run.log 2>&1 &
sleep "$1"
grep -iE "error|exception" logs/app-run.log | head -20 || true
w=$(xdotool search --name "Dogeometric" | head -1)
xdotool windowactivate --sync "$w" >/dev/null 2>&1 || true
sleep 0.8
maim -i "$w" /tmp/dogeometric-shot.png
REMOTE
"${SSH[@]}" 'cat /tmp/dogeometric-shot.png' > "$OUT"
echo "screenshot: $OUT"
