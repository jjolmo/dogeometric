#!/usr/bin/env bash
# Build Manifold's C API (libmanifoldc) for the current platform into app/native/<rid>/.
# The libraries find each other next to themselves ($ORIGIN, @loader_path, or the DLL's folder on Windows), so
# they can ship beside the executable. macOS gets one universal (arm64 + x86_64) build.
# The C API needs CrossSection (and Clipper2, fetched and linked statically).
# Manifold: https://github.com/elalish/manifold (Apache-2.0). Pinned to a release tag.
set -euo pipefail
TAG="${MANIFOLD_TAG:-v3.2.1}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${TMPDIR:-/tmp}/manifold-build-$TAG"
EXTRA=()
GENERATOR=(-G Ninja)
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) RID=linux-x64; EXTRA=(-DCMAKE_INSTALL_RPATH='$ORIGIN') ;;
  Linux-aarch64) RID=linux-arm64; EXTRA=(-DCMAKE_INSTALL_RPATH='$ORIGIN') ;;
  Darwin-*) RID=osx; EXTRA=(-DCMAKE_INSTALL_RPATH='@loader_path' '-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64' -DCMAKE_OSX_DEPLOYMENT_TARGET=10.15) ;;
  MINGW*|MSYS*|CYGWIN*) RID=win-x64; GENERATOR=(-A x64); EXTRA=(-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded) ;;
  *) echo "unsupported platform; build manually" >&2; exit 1 ;;
esac
[ -d "$WORK/src" ] || git clone --depth 1 --branch "$TAG" https://github.com/elalish/manifold.git "$WORK/src"
cmake -S "$WORK/src" -B "$WORK/build" "${GENERATOR[@]}" -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON -DMANIFOLD_CBIND=ON -DMANIFOLD_PAR=OFF -DMANIFOLD_TEST=OFF \
  -DMANIFOLD_PYBIND=OFF -DMANIFOLD_JSBIND=OFF -DMANIFOLD_CROSS_SECTION=ON -DMANIFOLD_USE_BUILTIN_CLIPPER2=ON -DMANIFOLD_STRICT=OFF \
  -DCMAKE_BUILD_WITH_INSTALL_RPATH=ON "${EXTRA[@]}"
cmake --build "$WORK/build" --config Release
mkdir -p "$ROOT/app/native/$RID"
find "$WORK/build" \( -name "libmanifold*.so*" -o -name "libmanifold*.dylib" -o -name "manifold*.dll" \) -not -path "*/CMakeFiles/*" \
  -exec cp -P {} "$ROOT/app/native/$RID/" \;
ls -la "$ROOT/app/native/$RID"
