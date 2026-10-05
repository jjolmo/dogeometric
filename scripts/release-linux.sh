#!/usr/bin/env bash
# Build the Linux x86_64 release (tarball and AppImage under dist/) from the Godot .NET export plus Manifold's libraries.
# Needs Godot 4.7 .NET with mono export templates (GODOT) and app/native/linux-x64; fetches appimagetool if missing.
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

# AppImage: the same files under usr/lib, started by AppRun.
TOOL="${APPIMAGETOOL:-$HOME/.cache/dogeometric/appimagetool}"
if [ ! -x "$TOOL" ]; then
  mkdir -p "$(dirname "$TOOL")"
  curl -sL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x "$TOOL"
fi
APPDIR="$ROOT/dist/Dogeometric.AppDir"
rm -rf "$APPDIR" && mkdir -p "$APPDIR/usr/lib" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp -a "$OUT" "$APPDIR/usr/lib/dogeometric"
cp "$ROOT/docs/branding/icon-256.png" "$APPDIR/dogeometric.png"
cp "$ROOT/docs/branding/icon-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/dogeometric.png"
cat > "$APPDIR/dogeometric.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Dogeometric
Comment=3D modelling that works like SketchUp 2021
Exec=dogeometric %F
Icon=dogeometric
Categories=Graphics;3DGraphics;
MimeType=application/vnd.sketchup.skp;
DESKTOP
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
# The game does not keep the caller's folder: files given by relative path go in absolute.
for arg in "$@"; do
  shift
  if [ -e "$arg" ]; then set -- "$@" "$(readlink -f "$arg")"; else set -- "$@" "$arg"; fi
done
exec "$HERE/usr/lib/dogeometric/dogeometric.x86_64" -- "$@"
APPRUN
chmod +x "$APPDIR/AppRun"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$APPDIR" "$ROOT/dist/Dogeometric-$VERSION-x86_64.AppImage" >/dev/null
echo "$ROOT/dist/Dogeometric-$VERSION-x86_64.AppImage"
