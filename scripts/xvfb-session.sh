#!/usr/bin/env bash
# Run Dogeometric on an invisible X display (software Vulkan), for scripted UI checks that don't touch the desktop.
# Usage: scripts/xvfb-session.sh start [file]   → display :99, app running
#        scripts/xvfb-session.sh shot out.png   → screenshot of the whole virtual screen
#        scripts/xvfb-session.sh stop
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DISPLAY_NUM=${XVFB_DISPLAY:-:99}
GODOT="${GODOT:-$HOME/Godot/godot.x86_64}"
case "${1:-}" in
  start)
    pkill -f "[X]vfb $DISPLAY_NUM" || true
    Xvfb $DISPLAY_NUM -screen 0 1600x960x24 >/dev/null 2>&1 &
    sleep 1
    cd "$ROOT"
    dotnet build app/Dogeometric.csproj -nologo -v q | grep -E "error|Build succeeded" | sort -u
    "$GODOT" --headless --path app --import >/dev/null 2>&1 || true
    # Own user:// data (toolbar layout, shortcuts) so test runs never touch the real profile.
    DISPLAY=$DISPLAY_NUM VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json XDG_DATA_HOME=/tmp/dogeometric-xvfb-data DOGEOMETRIC_NO_NATIVE_DIALOGS=1 \
      nohup "$GODOT" --path app --resolution 1600x960 --position 0,0 -- ${2:+"$2"} > /tmp/dogeometric-xvfb.log 2>&1 &
    sleep 8
    echo "running on $DISPLAY_NUM"
    ;;
  shot)
    DISPLAY=$DISPLAY_NUM import -window root "${2:-/tmp/dogeometric-xvfb.png}"
    ;;
  stop)
    pkill -f "[g]odot.x86_64 --path app --resolution 1600x960" || true
    pkill -f "[X]vfb $DISPLAY_NUM" || true
    ;;
esac
