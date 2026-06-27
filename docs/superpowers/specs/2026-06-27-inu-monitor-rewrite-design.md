# INU-MONITOR Rust UI Rewrite — Design Spec

**Date:** 2026-06-27
**Status:** Approved (user-directed full rewrite, all features, full TDD)

## Goal

Rewrite the Rust app's React frontend (`rust/src`) to faithfully match the
`docs/sample ui/INU Monitor (standalone-src).html` design — a mission-control
"INU-MONITOR" telemetry console — with **all** its interactive features, wired
to the live WebSocket telemetry. The Rust backend, and the data layer
(`ws/`, `data/`, `format/`, `hud/`, `gauge/`, `types.ts`), stay; the `ui/`
component tree is rebuilt.

The design source of truth is `docs/sample ui/INU Monitor (standalone-src).html`
(markup + the `text/x-dc` logic class) and the screenshots in
`docs/sample ui/screenshots/`.

## Look & feel (exact values from the source)

- Background `#0a0e14`; panels `#10151d` / `#0c121a`; panel header `#131a24`;
  borders `#1d2632` / `#1a2230`; column-header bg `#0c1119`.
- Text: data `#c3ccd8` / `#cdd6e1`; dim `#566273` / `#5d6b7c`; addr `#4f5a68`;
  panel-title `#8b98a9`.
- Accents: cyan `#38c5e0` (primary, default accent `#37c0dd`), value-green
  `#2fd17a` / `#38d178`, caution amber `#f5b440` / `#f5c061`, alarm red
  `#ff4d52` / `#ff6b70`. Critical row bg `#1a0e11`.
- Fonts: `'IBM Plex Sans'` (UI), `'IBM Plex Mono'` (data/numbers). Bundle via
  `@fontsource` (offline-safe).
- Dense, fixed-ish console layout; custom dark scrollbars; subtle 1px borders;
  small radii (4–9px).

## Screens (tabs)

- **OVERVIEW** — left grouped PARAMETERS panel (~31 channels) + a
  drag/resize/add/remove widget grid (gauge / line / map) + bottom transport
  timeline.
- **FLIGHT TRACK** — large flight-track map (SVG track + optional Leaflet OSM)
  + present-position readouts + waypoints list.
- **EVENTS** — event log table + summary cards + active-faults acknowledge.

## Data wiring (live vs derived)

The sample uses synthetic/static data; this app has live telemetry. Mapping:

| Sample element | Source in this app |
|---|---|
| Param rows / values | `store.channels()` + `store.latest(id)` + `formatValue` |
| Param groups (INU Mode, Velocity, Attitude, Acceleration, Body Rates, System) | derived from channel name/unit (a static grouping table keyed by `column_name`) |
| Status dot color | enum severity (`store.enumIndex`) or value-vs-min/max band; critical/caution/ok |
| Gauge value + auto-scale + needle | `store.latest(id)` + the source's gauge math (nice-round R, −135..+135°) |
| Line widget series | `store.series(id).arrays()` (real `ChannelSeries`), not the seeded RNG |
| Flight-track path / position | `store.gpsTrack()` (real lat/lon), projected to the SVG/Leaflet frame |
| Clock | `Date.now()` / ride time from `store.lastEmitUnixMs` |
| Alarm / caution counts | derived: enum-`critical` channels → alarm; configured threshold bands → caution |
| Events log, waypoints, faults | derived from severity transitions + a small static seed where the telemetry has no equivalent (clearly marked) |
| Perf HUD (FPS/frame/latency/CPU/RAM) | kept from the current app (`FpsMeter`, metrics frames) — an addition to the design |

Charts: port the source's **SVG line widgets** (zoom + hover tooltip + axis
labels) rather than uPlot — faithful to the design and to the interactions.
The perf HUD still measures FPS/latency regardless of renderer.

## Interactions (all in scope)

Tab switching; param-row drag → add gauge; widget drag → reorder/move on the
grid; pointer-drag resize (cols×rows, gauges square); gauge↔line toggle;
widget remove; line-chart context-menu zoom (in/out/reset) + hover crosshair
tooltip; map OSM↔grid toggle; SCALES on/off; accent color; map overlay
(rings/grid/basemap) toggles; transport timeline scrubber.

## Phased delivery (one branch + PR per phase)

1. **Shell** — theme + fonts + top bar (logo/id/tabs/alarm·caution·link/clock/scales) + tab routing + bottom transport timeline. Tab views are stubs.
2. **Param panel** — grouped live param table with status dots, color states, critical highlight, ALL/CH count.
3. **Widget grid** — gauge + SVG line + map widgets rendering live data; default overview layout; gauge auto-scale + needle math; SVG line path + axes.
4. **Interactions** — drag-add, drag-reorder/move, resize, gauge↔line toggle, remove, line zoom (context menu) + hover tooltip.
5. **Flight Track view** — big map (SVG track + OSM toggle), present-position readouts (live), waypoints list.
6. **Events view** — event log (severity-derived), summary cards, active-faults acknowledge.
7. **Polish** — overlay toggles (rings/grid/basemap), accent picker, Leaflet OSM integration, perf HUD overlay, pixel pass vs the screenshots.

## Process

Full TDD subagent-driven per phase (failing test → impl → task review → final
whole-branch review → PR), matching the prior six plans. Pure logic (clock,
status derivation, grouping, gauge/line math, projection, transport math) is
vitest-tested; React components get jsdom smoke tests; canvas/Leaflet visuals
are build-verified + confirmed by launching the app. Each phase ends with a
live visual check against the screenshots.

## Non-goals

No change to the Rust backend or the tested data layer. No new telemetry
channels. The `.NET` app is out of scope here (it gets the same design later
if desired).
