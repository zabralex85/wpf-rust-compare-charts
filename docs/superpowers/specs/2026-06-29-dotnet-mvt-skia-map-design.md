# .NET native MVT Skia Map Renderer — Design Spec

**Date:** 2026-06-29
**Status:** Approved (user-directed)

## Goal

Render the offline INU map in the native WPF .NET app by drawing the **existing
vector `israel.mbtiles`** (MVT/OpenMapTiles schema) directly with SkiaSharp —
water / landuse / roads / buildings + labels + the live GPS track — in a dark
INU style mirroring the Rust `mapStyle`. Fully offline, fully native (no
WebView), reading the MBTiles in-process via SQLite.

This **replaces the Mapsui-based Phase 5** of the .NET reskin: Mapsui 4.1.9 (and
BruTile) render only **raster** tiles; `israel.mbtiles` is **vector** (MVT), so
Mapsui cannot draw it. No off-the-shelf native-WPF library renders MVT vector
tiles, so we build a small purpose-fit renderer.

Source of truth for the look: `rust/src/ui/app/widgets/mapStyle.ts` +
`MapWidget.tsx`. Reuse the same `israel.mbtiles` and the Rust MBTiles read
conventions (TMS→XYZ Y-flip, gzip MVT).

## Why custom

- Mapsui/BruTile/GMap.NET = raster only. The tileset is vector MVT.
- MapLibre/Mapbox GL render MVT but are web/WebGL (the Rust app uses them inside
  a WebView). The .NET app is deliberately native (no WebView).
- `Mapbox.VectorTile` (pure C#) decodes MVT → geometry; SkiaSharp draws it;
  `SkiaSharp.Views.WPF` hosts a native `SKElement`. All offline, all native.

## Components (isolation)

New class library **`TelemetryPoc.Map`** (net8.0; references SkiaSharp,
SkiaSharp.Views.WPF, Mapbox.VectorTile, Microsoft.Data.Sqlite) — all map logic,
isolated from WPF and (where pure) xUnit-tested. The WPF view stays thin.

```
TelemetryPoc.Map/
  Region.cs        — immutable camera frame: record (CenterLat, CenterLon, int Zoom, double Width, double Height).
  WebMercator.cs    — lon/lat → world-pixel(zoom); TileXY(lon,lat,z); pixel-within-tile. Pure (xUnit).
  TileMath.cs       — VisibleTiles(region) → tiles (z,x,y)+screen offsets; FitBbox(track bbox, viewport) → (center, zoom) clamped to [9,14]. Pure (xUnit).
  MapStyle.cs       — source-layer → paint (color / width / fill|line) table, mirrors rust mapStyle. Pure data.
  MbTilesReader.cs  — open israel.mbtiles (SQLite, read-only); Read(z,x,y) → MVT bytes: TMS Y-flip row=(1<<z)-1-y, gunzip. Pure-ish (xUnit vs a synthetic db).
  MvtBasemap.cs     — decode tiles (Mapbox.VectorTile) → features; transform tile-local→web-mercator→screen; draw geometry + labels onto an SKCanvas in MapStyle order; returns/records an SKPicture. Per-feature transform testable; Skia draw build-verified.
  TrackOverlay.cs   — project GPS lat/lon → screen via the Region; draw cyan polyline + position marker onto an SKCanvas.

dotnet/src/TelemetryPoc.App/
  ViewModels/MapWidgetViewModel.cs — exposes the GPS track (lat/lon) + the computed Region; raises Updated on tick.
  Views/MapWidgetView.xaml(.cs)    — hosts SkiaSharp SKElement; computes Region ONCE from the track bbox, loads+decodes tiles, renders the basemap to a cached SKPicture; each tick: draw cached basemap + TrackOverlay.
```

## Camera (Region) — computed once

On `MetaLoaded` (all samples available): `TileMath.FitBbox` takes the GPS
track's bounding box (min/max lat/lon over the whole track), sets center = bbox
center, picks the largest available zoom `z ∈ [9,14]` at which the bbox fits the
cell (with padding). The Region (center + zoom + size) is then **frozen** — the
camera never moves (static region). If the track is degenerate (one point /
empty), fall back to a default center (Tel Aviv 32.0853 N, 34.7818 E) at z12.

## Render pipeline + basemap cache

**Basemap — rendered once, cached:**
1. `TileMath.VisibleTiles(region)` → the tiles covering the viewport + each
   tile's pixel offset on the canvas.
2. Per tile: `MbTilesReader.Read(z,x,y)` (SQLite blob, Y-flip, gunzip) →
   `Mapbox.VectorTile` decode → layers/features (tile-local 0..extent).
3. Draw in MapStyle order (background → water → landcover → landuse →
   transportation-casing → transportation → building → labels). Each feature:
   tile-local → web-mercator → screen (WebMercator + tile offset), then SKCanvas
   fill (polygons) / stroke (lines) with the layer's paint. Labels: place points
   + transportation_name line-midpoints, `name:latin` (fallback `name`), text +
   halo, greedy anti-collision (skip a label whose bbox overlaps a placed one).
4. Record the result into an **`SKPicture`** (basemap cache). The heavy work
   (decode + draw + label placement) happens **once**.

**Each tick (Updated):** `OnPaintSurface` draws the cached basemap (one draw)
then `TrackOverlay`: project the current lat/lon track → screen, cyan polyline
(width ~3) + position marker (circle) + optional geo-chrome. Cheap — only the
overlay redraws.

**Sizing:** `SKElement` sizes itself (it does not collapse like the MapLibre
flex chain), but `MapWidgetView` still guards against a zero size before
computing the Region; if 0, it defers until the first valid `SizeChanged`.

**Errors / empty:** missing tileset / no tiles in db → empty basemap (dark
`#0a0e14`), the track still draws on top. A corrupt tile → skipped (per-tile
try/catch), never crashes the map.

## Style (MapStyle, mirrors rust mapStyle)

source-layer → paint:
- `background` `#0a0e14`; `water` fill `#16384f`; `landcover` fill `#0c1118`;
  `landuse` fill `#111820`; `transportation-casing` line `#0a0e14` (wider);
  `transportation` line `#5b6470`; `building` fill `#232d38`.
- Labels: `place` text `#aebccd` halo `#0a0e14`; `transportation_name` text
  `#8a99ad` halo `#0a0e14`; mono/sans font, `name:latin` (fallback `name`).
- Track: cyan `#38c5e0` width ~3; position marker green `#2fd17a`.

## Data wiring

`MapWidgetViewModel` reads `store.GpsTrack()` (the `(Lat, Lon)` parallel lists)
each tick and raises `Updated`. The Region is computed once from the full track
(the player has all samples; the bbox is stable). The track polyline grows as
the replay advances; the basemap stays fixed.

## Testing

`TelemetryPoc.Map` pure logic is xUnit-tested (in the existing
`TelemetryPoc.Core.Tests`): `WebMercator` (known lon/lat→pixel, round-trip),
`TileMath` (VisibleTiles set + offsets; FitBbox center/zoom + clamp),
`MbTilesReader` (Y-flip formula + query against a tiny synthetic SQLite db with
one gzip blob), `MapStyle` (layer→paint). The MVT decode + Skia drawing + WPF
host are build-verified + confirmed by launching the app against the real
`israel.mbtiles`. (The committed `fixture.mbtiles` has dummy tiles — usable for
the reader's query/Y-flip test, not for decode.)

## Phased delivery (one branch + PR per phase, subagent-driven TDD)

1. **`TelemetryPoc.Map` foundation** — project + deps; `WebMercator` +
   `TileMath` (FitBbox + VisibleTiles) + `Region` + `MapStyle` (pure
   math/data, xUnit). No drawing.
2. **MBTiles + MVT decode** — `MbTilesReader` (SQLite, Y-flip, gunzip; xUnit vs
   a synthetic db) + `MvtBasemap` decode (Mapbox.VectorTile → features) + the
   per-feature tile-local→screen transform (tested) up to "geometry in screen
   coordinates".
3. **Skia basemap + track + WPF host** — `MvtBasemap` draws geometry + labels
   into an `SKPicture` cache; `TrackOverlay`; `MapWidgetView` (SKElement, Region
   once from bbox, basemap cache, overlay per tick) + `MapWidgetViewModel`; wire
   into OverviewView. Build-verified + launch-confirmed against `israel.mbtiles`.

## Non-goals

No pan/zoom/click interaction (perf-focus, static region). No online tiles. No
glyph PBFs or tile HTTP server (direct SQLite + Skia text). No changes to
`TelemetryPoc.Core` or the Rust app. Label typography/collision is a basic
greedy pass, not production-grade placement.
