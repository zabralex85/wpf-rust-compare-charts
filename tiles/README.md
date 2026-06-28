# Offline Israel vector tiles

## Startup provisioning (automatic, blocking first run)
On launch the app ensures `tiles/israel.mbtiles` exists, in order:
1. `RIDE_MBTILES` (explicit path) or `tiles/israel.mbtiles` already present → used.
2. `RIDE_MBTILES_URL` set → downloaded (blocking, console progress) on first run.
3. else `tilemaker` on PATH + `RIDE_TILEMAKER_CONFIG`/`RIDE_TILEMAKER_PROCESS`
   → downloads `RIDE_PBF_URL` (default geofabrik Israel) + converts.
4. else → no basemap (SVG map only); the console prints these instructions.

Labels render from bundled glyphs (see below). The committed `fixture.mbtiles` has dummy
tiles for cargo tests only — it is **not** used as a runtime basemap.

## Build israel.mbtiles (~80 MB, gitignored)
```bash
wget https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf
tilemaker --input israel-and-palestine-latest.osm.pbf \
  --output israel.mbtiles --config config.json --process process.lua
```
`config.json` / `process.lua`: tilemaker's OpenMapTiles-compatible resources (use the
ones bundled with the tilemaker release you run).

> **Windows — use tilemaker v2.4.0, not v3.0.0.** v3.0.0 crashes on Windows with
> `STATUS_STACK_BUFFER_OVERRUN`, and its `process.lua` calls a `Find` global the
> v2.4.0 runtime does not provide. Download the **v2.4.0** Windows zip and run it with
> **its own** bundled `config.json`/`process.lua`. A real israel extract yields
> ~81 MB / ~12 800 tiles (z0–14), OMT schema (water/transportation/building/waterway/
> place; no landcover/landuse without Natural Earth shapefiles — harmless). The
> geofabrik israel-and-palestine `.osm.pbf` is ~120 MB.

## Glyphs (offline labels, gitignored)
The map style renders text from glyph PBFs (tilemaker does NOT produce them). Fetch the
Noto Sans set into `tiles/glyphs/<fontstack>/<range>.pbf`:
```bash
./fetch-glyphs.sh          # downloads openmaptiles/fonts noto-sans.zip → tiles/glyphs/
```
The style's `place`/`transportation_name` labels use the romanized `name:latin`
attribute (`Noto Sans Regular` has no Hebrew glyphs), falling back to the native `name`.

## Wire the app
```bash
RIDE_MBTILES=tiles/israel.mbtiles RIDE_GLYPHS=tiles/glyphs RIDE_TILES_PORT=9002
```

## Test fixture
```bash
python make_fixture.py   # writes tiles/fixture.mbtiles (committed; used by cargo tests)
```
