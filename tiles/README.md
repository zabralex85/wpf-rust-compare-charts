# Offline Israel vector tiles

## Build israel.mbtiles (~1–3 GB, gitignored)
```bash
wget https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf
tilemaker --input israel-and-palestine-latest.osm.pbf \
  --output israel.mbtiles --config config.json --process process.lua
```
`config.json` / `process.lua`: tilemaker's OpenMapTiles-compatible resources.

## Glyphs (offline labels, gitignored)
Noto Sans Regular/Bold glyph PBFs from the openmaptiles/fonts build → `tiles/glyphs/<fontstack>/<range>.pbf`

## Wire the app
```bash
RIDE_MBTILES=tiles/israel.mbtiles RIDE_GLYPHS=tiles/glyphs RIDE_TILES_PORT=9002
```

## Test fixture
```bash
python make_fixture.py   # writes tiles/fixture.mbtiles (committed; used by cargo tests)
```
