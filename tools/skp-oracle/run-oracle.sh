#!/usr/bin/env bash
# Ground-truth counts for every .skp under a folder, from SketchUp 2021's Ruby API (Bottles bottle "SketchUp").
# One SketchUp launch per file; oracle.rb appends a JSON line per file to oracle.jsonl and kills SketchUp.
# Usage: run-oracle.sh <linux-folder> <same folder as a Windows path, e.g. C:\users\steamuser\Documents\x>
set -u
LIN_DIR="$1"; WIN_DIR="$2"
export DISPLAY=${DISPLAY:-:0} XAUTHORITY=${XAUTHORITY:-$HOME/.Xauthority}
cd "$LIN_DIR"
touch oracle.jsonl
find . -iname '*.skp' | sort | while read -r rel; do
  rel="${rel#./}"
  win="$WIN_DIR\\${rel//\//\\}"
  # Already done (resumable)? Match the whole path as oracle.rb writes it (JSON-escaped backslashes): files with the
  # same name in different folders are different files.
  key="\"file\":\"${win//\\/\\\\}\""
  grep -qF "$key" oracle.jsonl && continue
  timeout 300 flatpak run --command=bottles-cli com.usebottles.bottles run -b SketchUp -p 'SketchUp 2021' -- \
    -RubyStartup "$WIN_DIR\\oracle.rb" "$win" < /dev/null > /dev/null 2>&1  # wine would eat the file list on stdin
  if ! grep -qF "$key" oracle.jsonl; then
    echo "{\"file\": \"$rel\", \"error\": \"SketchUp did not open it (timeout or crash)\"}" >> oracle.jsonl
  fi
  pkill -f '[S]ketchUp 2021/SketchUp.exe'; pkill -f '[s]ketchup_webhelper'
  sleep 1
done
echo "oracle done"
