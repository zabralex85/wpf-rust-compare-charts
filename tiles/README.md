# Offline Israel vector tiles

## Startup provisioning (automatic, blocking first run)
On launch the app ensures `tiles/israel.mbtiles` exists, in order:
1. `RIDE_MBTILES` (explicit path) or `tiles/israel.mbtiles` already present → used.
2. `RIDE_MBTILES_URL` set → downloaded (blocking, console progress) on first run.
3. else `tilemaker` on PATH + `RIDE_TILEMAKER_CONFIG`/`RIDE_TILEMAKER_PROCESS`
   → downloads `RIDE_PBF_URL` (default geofabrik Israel) + converts.
4. else → no basemap (SVG map only); the console prints these instructions.

Labels are skipped for now (no glyphs). The committed `fixture.mbtiles` has dummy
tiles for cargo tests only — it is **not** used as a runtime basemap.

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
