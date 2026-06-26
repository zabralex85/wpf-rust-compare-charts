# Dashboard Reference Target

This is the visual target both the Rust and .NET dashboards must replicate (an INU
telemetry monitoring UI). The original screenshot was provided in chat; re-attach it
to store `docs/reference/dashboard-target.png` beside this file. Until then, this
written description is the canonical layout spec for the UI plans.

## Overall layout

A dense, single-window dashboard on a light/grey chrome with multiple framed panels.
Four main regions:

```
+---------------------+-------------------------------+------------------+
| PARAM TABLE (left)  |  MAIN STRIP CHART (center-top) |  GPS MAP (top-r) |
|  ~30 rows           |  black bg, red/white bars      |  OSM tiles +     |
|  Param | Eng.Data   |  y-axis ~ -8 .. +7.7           |  track + markers |
|  | Bar | Addr       |                               +------------------+
|                     +-------------------------------+  GAUGE: SkyPitch  |
|                     |  BOTTOM STRIP CHART           |  (radial, g)     |
|                     |  blue-filled area, white line +------------------+
|                     |  "AA123 I0116PlatAccZ"        |  GAUGE: roll "SC" |
+---------------------+-------------------------------+------------------+
| STATUS BAR: timestamp (10:38:43:358.944)  |  rate (1.0 s/s)  | progress |
+----------------------------------------------------------------------+
```

## Panels in detail

### 1. Param table (left column)
- Columns: **Parameter** (name, e.g. `I0101INUMode1`, `I0110Roll`), **Eng. Data**
  (numeric value, right-aligned, green text), **Bar** (small horizontal bar gauge,
  green fill proportional to value), **Addr** (e.g. `I_01`, `I_09`, `BI_L`).
- ~30 rows, compact monospace, header row highlighted.
- Enum/severity rows stand out: e.g. `SkyINUMode1` = `Normal` on green, `SkyINUMode2`
  = `Critical` on a red bar. This is the severity-color requirement.

### 2. Main strip chart (center, upper)
- Black background, fine grid. Title bar shows live readout (`I0110Roll=24.0868 ...`).
- Series rendered as vertical **red and white bars / hi-lo ticks** (min-max style),
  busy and fast-scrolling. Y axis labeled ~ `-8.07 .. +7.68`.

### 3. Bottom strip chart (center, lower)
- Title `AA123 I0116PlatAccZ=...`. **Blue filled area** under a thin white line,
  green grid, x-axis 0..~5 (seconds window). A second scrolling time-series panel.

### 4. GPS map (top-right)
- OpenStreetMap-style tile basemap over the Tel Aviv / Rishon LeZion / Petah Tikva
  area. Numbered **waypoint markers** (boxed labels) and an aircraft/vehicle glyph at
  the current position. Axes/grid overlaid (`-8 .. +14`, `-50 .. +38`).

### 5. Gauges (right column)
- **SkyPitch** radial gauge, scale in **g** (`-4.0 .. +4.0`), orange needle.
- **Gauge "SC"** radial (roll), scale `-180 .. +180`, cyan needle.
- Round bezels, dark face, tick labels.

### 6. Status bar (bottom)
- Left: running timestamp `10:38:43:358.944`. Replay rate `1.0 s/s`. A wide progress
  bar for ride position.

## What "align with the sample" means for acceptance
- Same four-region layout and relative proportions.
- Param table with the value/bar/addr columns and enum severity coloring.
- Two strip-chart panels (one bar/tick style, one filled-area style).
- Radial gauges with the g and degree scales.
- GPS map with OSM tiles + track + markers.
- HUD overlay (FPS / frame time / latency / CPU / RAM) — this is an addition to the
  original image, for the perf comparison, placed in a corner.
