#!/usr/bin/env bash
# Build the release packages under dist/ from the Godot .NET export, on Linux:
#   linux   → tarball and AppImage      windows → zip      macos → zip with a universal, ad-hoc signed .app
# Usage: scripts/release.sh [linux] [windows] [macos]   (all three by default)
# Needs Godot 4.7 .NET (GODOT) with its export templates, and app/native/<rid> built for each platform
# (tools/native/build-manifold.sh); the export publishes those libraries beside the assemblies.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT="${GODOT:-$HOME/Godot/godot.x86_64}"
VERSION="$(sed -n 's/^config\/version="\(.*\)"/\1/p' "$ROOT/app/project.godot")"
PLATFORMS=("$@")
[ ${#PLATFORMS[@]} -gt 0 ] || PLATFORMS=(linux windows macos)
cp "$ROOT/scripts/export_presets.cfg" "$ROOT/app/export_presets.cfg"
mkdir -p "$ROOT/dist"
"$GODOT" --headless --path "$ROOT/app" --import >/dev/null 2>&1 || true

need_native() {
  if ! ls "$ROOT/app/native/$1/"*manifold* >/dev/null 2>&1; then
    echo "app/native/$1 is missing: build it with tools/native/build-manifold.sh on that platform" >&2
    exit 1
  fi
}

linux() {
  need_native linux-x64
  local name="dogeometric-$VERSION-linux-x86_64" out="$ROOT/dist/dogeometric-$VERSION-linux-x86_64"
  rm -rf "$out" && mkdir -p "$out"
  "$GODOT" --headless --path "$ROOT/app" --export-release "Linux" "$out/dogeometric.x86_64"
  cp "$ROOT/LICENSE" "$ROOT/README.md" "$out/"
  tar -C "$ROOT/dist" -czf "$ROOT/dist/$name.tar.gz" "$name"
  echo "$ROOT/dist/$name.tar.gz"

  # AppImage: the same files under usr/lib, started by AppRun.
  local tool="${APPIMAGETOOL:-$HOME/.cache/dogeometric/appimagetool}"
  if [ ! -x "$tool" ]; then
    mkdir -p "$(dirname "$tool")"
    curl -sL -o "$tool" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
    chmod +x "$tool"
  fi
  local appdir="$ROOT/dist/Dogeometric.AppDir"
  rm -rf "$appdir" && mkdir -p "$appdir/usr/lib" "$appdir/usr/share/icons/hicolor/256x256/apps"
  cp -a "$out" "$appdir/usr/lib/dogeometric"
  cp "$ROOT/docs/branding/icon-256.png" "$appdir/dogeometric.png"
  cp "$ROOT/docs/branding/icon-256.png" "$appdir/usr/share/icons/hicolor/256x256/apps/dogeometric.png"
  cat > "$appdir/dogeometric.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Dogeometric
Comment=3D modelling that works like SketchUp 2021
Exec=dogeometric %F
Icon=dogeometric
Categories=Graphics;3DGraphics;
MimeType=application/vnd.sketchup.skp;
DESKTOP
  cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
# The game does not keep the caller's folder: files given by relative path go in absolute.
for arg in "$@"; do
  shift
  if [ -e "$arg" ]; then set -- "$@" "$(readlink -f "$arg")"; else set -- "$@" "$arg"; fi
done
exec "$HERE/usr/lib/dogeometric/dogeometric.x86_64" -- "$@"
APPRUN
  chmod +x "$appdir/AppRun"
  ARCH=x86_64 "$tool" --appimage-extract-and-run "$appdir" "$ROOT/dist/Dogeometric-$VERSION-x86_64.AppImage" >/dev/null
  echo "$ROOT/dist/Dogeometric-$VERSION-x86_64.AppImage"
}

windows() {
  need_native win-x64
  local name="Dogeometric-$VERSION-windows-x86_64" out="$ROOT/dist/Dogeometric-$VERSION-windows-x86_64"
  rm -rf "$out" && mkdir -p "$out"
  "$GODOT" --headless --path "$ROOT/app" --export-release "Windows" "$out/Dogeometric.exe"
  cp "$ROOT/LICENSE" "$ROOT/README.md" "$out/"
  (cd "$ROOT/dist" && rm -f "$name.zip" && zip -qr "$name.zip" "$name")
  echo "$ROOT/dist/$name.zip"
}

macos() {
  need_native osx
  local name="Dogeometric-$VERSION-macos-universal"
  rm -f "$ROOT/dist/$name.zip"
  "$GODOT" --headless --path "$ROOT/app" --export-release "macOS" "$ROOT/dist/$name.zip"
  (cd "$ROOT" && zip -qj "dist/$name.zip" LICENSE README.md)
  echo "$ROOT/dist/$name.zip"
}

for p in "${PLATFORMS[@]}"; do
  "$p"
done
