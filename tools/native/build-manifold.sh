#!/usr/bin/env bash
# Build Manifold's C API (libmanifoldc) for the current platform into app/native/<rid>/.
# The C API needs CrossSection (and Clipper2, fetched and linked statically).
# Manifold: https://github.com/elalish/manifold (Apache-2.0). Pinned to a release tag.
set -euo pipefail
TAG="${MANIFOLD_TAG:-v3.2.1}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${TMPDIR:-/tmp}/manifold-build-$TAG"
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) RID=linux-x64; LIB=libmanifoldc.so ;;
  Linux-aarch64) RID=linux-arm64; LIB=libmanifoldc.so ;;
  Darwin-*) RID=osx; LIB=libmanifoldc.dylib ;;
  *) echo "unsupported platform; build manually" >&2; exit 1 ;;
esac
[ -d "$WORK/src" ] || git clone --depth 1 --branch "$TAG" https://github.com/elalish/manifold.git "$WORK/src"
cmake -S "$WORK/src" -B "$WORK/build" -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON -DMANIFOLD_CBIND=ON -DMANIFOLD_PAR=OFF -DMANIFOLD_TEST=OFF \
  -DMANIFOLD_PYBIND=OFF -DMANIFOLD_JSBIND=OFF -DMANIFOLD_CROSS_SECTION=ON -DMANIFOLD_USE_BUILTIN_CLIPPER2=ON -DMANIFOLD_STRICT=OFF
cmake --build "$WORK/build"
mkdir -p "$ROOT/app/native/$RID"
find "$WORK/build" \( -name "libmanifold*.so*" -o -name "libmanifold*.dylib" \) -exec cp -P {} "$ROOT/app/native/$RID/" \;
ls -la "$ROOT/app/native/$RID"
