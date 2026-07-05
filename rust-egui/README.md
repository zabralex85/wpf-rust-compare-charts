# rust-egui — egui/eframe immediate-mode variant

The **fourth** dashboard variant, and the lightweight end of the comparison: an
**immediate-mode** Rust UI on [`egui`](https://github.com/emilk/egui)/`eframe`
(glow / OpenGL backend). No HTML/CSS layout engine, no Vello/WGPU compute
renderer, no `system-fonts` database — the opposite of the Dioxus-native
(Blitz/Vello) variant.

![INU dashboard rendered by rust-egui — config layout, offline MVT map with Hebrew labels](../docs/screenshots/rust-egui.png)

## Why it exists

To answer "why does the native app eat ~530 MB when a C++ app was ~20 MB?" —
the memory was the **framework, not the language**. This variant, built on the
leanest common Rust GUI stack, lands at:

| | RAM | CPU | Processes |
|---|---|---|---|
| **rust-egui** | **~102 MB** | ~3% (0% idle) | 1 |

vs Dioxus-native ~530 MB, .NET/Avalonia-Skia ~279 MB, Tauri/WebView2 ~600+ MB
(all same ride, `RIDE_SPEED=1`, Release, 30 s warm-up — see the root README
table). ~5× lighter than Dioxus-native, ~2.7× lighter than .NET, and
CPU-competitive with both (.NET/Skia ~4–5%, Tauri ~3–4%). Rust itself is as lean
as C++; the heavy variants pay for their rendering stack.

**CPU was ~13% until it repainted on _change_ instead of a fixed 30 Hz clock.**
Immediate mode re-projects the *entire* MVT basemap (polygons, roads, labels)
every frame, so redrawing 30×/s — even while paused — was the whole cost (a
per-tab profile showed the empty EVENTS tab at ~1.8% and the map tabs at ~13%,
*unchanged* by pausing). Waking only at the data cadence (`request_repaint_after`
at ~10 Hz while playing, and nothing while idle — egui still repaints on
input) dropped it to **~3% playing / 0% idle** with no visible change: the ride
is 10 Hz, so 30 Hz never showed anything new. Caching the projected map geometry
(re-tessellate only on pan/zoom) would shave the playing figure further.

The residual RAM gap to a ~20 MB C++/Dear-ImGui app is `eframe`'s bundled default
fonts + a bundled Noto Sans Hebrew (map labels) + the glow GL context + `std`. A
`softbuffer` + `tiny-skia` (CPU raster) or raw Direct2D build would close most of it.

## Scope — full INU parity

Everything drawn with `egui::Painter`, no widgets library:
- **Frameless** shell (`with_decorations(false)`): draggable top bar + vector
  minimize / maximize / close.
- Dark INU theme + monospace; top bar (logo mark, centered **clickable tabs**,
  compact ALARM/CAUTION pills, LINK, ride clock); bottom transport bar —
  play/pause + stacked clock/T+ · speed, a clickable seek slider, then a stacked
  BUFFER / SAMPLES / DROPPED row.
- Grouped parameter table (INU groups + counts, status dots, BUS column, enum
  decode with severity color — `Normal`/`Critical`). Rows are **drag sources**.
- **Interactive widget grid**: drag to move, resize grip (corner cursors +
  a dashed placeholder ghost while dragging/resizing, like the .NET `DropGhost`),
  click the badge to toggle LINE↔GAUGE, `×` to remove, and **drag a param row
  into the grid to add** a chart (backfilled with history).
- **Config-driven start layout** — [`../data/dashboard-layout.json`](../data/dashboard-layout.json),
  a shared 8-col grid descriptor next to `ride.db` (`$RIDE_LAYOUT` overrides the
  path); missing/invalid falls back to the computed seed (mirrors Tauri's
  `seedLayout`: map 4×4 + gauges + lines, first-fit packed).
- Radial gauges (arc + needle + scale) and strip charts (y/x axes, gridlines,
  **hover readout** of the value under the cursor).
- GPS track over a **fixed pan/zoom offline slippy map** — filled MVT basemap
  (sea, parks, landuse, road hierarchy, place/street labels) decoded from
  `israel.mbtiles` with `geozero`/`flate2`, projected in web-mercator and drawn
  to the egui painter (`basemap.rs`). **Drag to pan, scroll to zoom**; tiles are
  re-fetched + cached per view. No wgpu/WebView — runs in egui's glow/GL context.
  `FLIGHT TRACK` tab shows it full-screen. Labels use the Hebrew `name` (bundled
  **Noto Sans Hebrew**, like the Tauri MapLibre style); POI/shop labels and
  pure-numeric road refs are dropped so streets/places stand out.

Reuses `app_lib` (db / `replay::Pacer` / `metrics` / `tiles::MbTiles`)
in-process. Almost all of it is one `src/main.rs` (+ `basemap.rs`) —
immediate-mode fits a single file.

## Run

```bash
cd rust-egui
RIDE_DB=../data/ride_small.db RIDE_SPEED=1 cargo run --release
```

## Notes

- Repaints are driven by **change, not a clock**: while playing, `update()` calls
  `request_repaint_after` at the data cadence (~10 Hz at `SPEED=1`); while paused
  it schedules nothing, so an idle window costs ~0% (egui still repaints on input
  — hover/drag/pan/zoom). A fixed 30 Hz clock previously re-rendered the map 30×/s
  even while paused and cost ~13% CPU for nothing.
- `default-features = false` on `eframe` (glow only, no wgpu) keeps it light.
- Depending on `app_lib` pulls the Tauri dep graph into the build (compile
  weight only; no WebView is created at runtime).
- Prototype: pure-logic isn't split into unit-tested modules like rust-native —
  it's a single-file spike to establish the footprint floor.
