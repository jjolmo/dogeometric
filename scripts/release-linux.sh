#!/usr/bin/env bash
# Build the Linux x86_64 release: Godot .NET export plus Manifold's libraries, packed as dist/dogeometric-<version>-linux-x86_64.tar.gz.
# Needs Godot 4.7 .NET (GODOT, default ~/Godot/godot.x86_64) with its mono export templates, and app/native/linux-x64 built.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT="${GODOT:-$HOME/Godot/godot.x86_64}"
VERSION="$(sed -n 's/^config\/version="\(.*\)"/\1/p' "$ROOT/app/project.godot")"
NAME="dogeometric-$VERSION-linux-x86_64"
OUT="$ROOT/dist/$NAME"

# export_presets.cfg is local to each checkout (git-ignored); write the release preset when it is missing.
if [ ! -f "$ROOT/app/export_presets.cfg" ]; then
  cat > "$ROOT/app/export_presets.cfg" <<'PRESET'
[preset.0]

name="Linux"
platform="Linux"
runnable=true
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter="data/*,cursors/*.json"
exclude_filter="native/*"
export_path=""

[preset.0.options]

binary_format/embed_pck=false
binary_format/architecture="x86_64"
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
PRESET
fi

rm -rf "$OUT" && mkdir -p "$OUT/native"
"$GODOT" --headless --path "$ROOT/app" --import >/dev/null 2>&1 || true
"$GODOT" --headless --path "$ROOT/app" --export-release "Linux" "$OUT/dogeometric.x86_64"
cp -P "$ROOT"/app/native/linux-x64/libmanifold*.so* "$OUT/native/"
cp "$ROOT/LICENSE" "$ROOT/README.md" "$OUT/"
tar -C "$ROOT/dist" -czf "$ROOT/dist/$NAME.tar.gz" "$NAME"
echo "$ROOT/dist/$NAME.tar.gz"
