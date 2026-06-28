#!/usr/bin/env bash
# Fetch Noto Sans glyph PBFs for offline map labels → tiles/glyphs/<fontstack>/<range>.pbf
# (gitignored; the Rust tile server serves them at /glyphs/{fontstack}/{range}.pbf).
# The dark map style uses the "Noto Sans Regular" fontstack.
set -euo pipefail
cd "$(dirname "$0")"

URL="https://github.com/openmaptiles/fonts/releases/download/v2.0/noto-sans.zip"
echo "Downloading Noto Sans glyphs from $URL ..."
curl -sL -o noto-sans.zip "$URL"
mkdir -p glyphs
unzip -o -q noto-sans.zip -d glyphs
rm -f noto-sans.zip
echo "Done. Fontstacks:"
ls glyphs
