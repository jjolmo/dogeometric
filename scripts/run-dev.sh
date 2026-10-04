#!/usr/bin/env bash
# Build Dogeometric from this checkout and run it. GODOT selects the Godot .NET binary (default: godot in PATH).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT="${GODOT:-godot}"
cd "$ROOT"
dotnet build app/Dogeometric.csproj -nologo -v q
# Import new or changed resources (icons, shaders) before running; incremental, so cheap.
"$GODOT" --headless --path app --import >/dev/null 2>&1 || true
exec "$GODOT" --path app -- "$@"
