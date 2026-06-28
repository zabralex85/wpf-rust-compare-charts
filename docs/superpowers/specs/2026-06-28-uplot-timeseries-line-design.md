# uPlot Time-Series Line Widget — Design Spec

**Status:** design (Phase 9 of the INU-MONITOR Rust UI work)

## Goal

Replace the hand-rolled SVG line widget with a **uPlot** time-series chart: a true time x-axis with a **scrolling window** (last N seconds), a stable y-axis auto-ranged to the windowed data, dark-themed to the INU palette, re-integrated into the editable widget grid (gauge↔line toggle, resize, remove, hover). Today's `LineChart` re-centers the y-axis on the live value each frame; the uPlot version shows the actual signal scrolling over real time.

## Why uPlot

uPlot is a tiny (~50 KB), fast canvas time-series library with a native time axis, built-in cursor/hover, and good streaming performance — the right tool for a scrolling strip chart and the original pre-rewrite pick. The SVG approach was chosen for the static INU look; for a true time-series the canvas engine is better.

## Architecture

`WidgetGrid` renders a line widget per the layout (Phase 3/4). We swap the inner `LineChart` to a uPlot-backed component **keeping the same prop contract** so the grid wiring, toggle/remove/resize, and the cell header are unchanged. uPlot is initialized into the cell once (guarded off in jsdom), its data `setData`'d as frames stream, and its x-scale held to a scrolling `[lastTs - window, lastTs]` range. The container is **explicitly sized in JS** (a ResizeObserver/fit) — the same WebView2 flex `%`-height collapse that hit the MapLibre map applies to the uPlot canvas.

## Components

### 1. Pure data prep — `rust/src/ui/app/widgets/uplotData.ts`
- `toUPlotData(xsMs: number[], ys: number[]): [number[], number[]]` — returns `[xsSeconds, ys]` (uPlot's `AlignedData`), `xsMs` converted to seconds, index-aligned, min-length-guarded. (uPlot wants the x series in seconds for its time axis.)
- `scrollWindow(xsMs: number[], windowMs: number): [number, number]` — the x-scale min/max in **seconds**: `[(last - windowMs)/1000, last/1000]`, or a sensible default when empty. `windowMs` derives from the widget zoom (`baseWindowMs / zoom`).
- Both pure + unit-tested (no canvas).

### 2. uPlot options builder — `rust/src/ui/app/widgets/uplotOpts.ts`
- `lineOpts(width, height, accent, opts): uPlot.Options` — dark theme: transparent background, x = time axis (relative seconds, grid at intervals), y auto-ranged to data with a small pad, stroke = `accent` (cyan), grid/axis styled with the INU palette **values** (uPlot styling is JS config = data, hardcoded hex acceptable, like the map style). Native cursor enabled for hover. No legend (or a minimal one). The exact palette values come from the theme (`#0a0e14` bg, `#38c5e0` accent, muted grid).
- Pure-ish (returns a config object) — a light unit test asserts the axis/scale/series shape (time x, y auto, stroke set).

### 3. Component — `rust/src/ui/app/widgets/LineChart.tsx` (rewritten)
- Same props as today: `{ name, xs, ys, unit, value, scalesOn, zoom?, onZoomBy?, onResetZoom? }` (so `WidgetGrid` is unchanged). `data-testid="linechart"` kept.
- On mount (guarded — see jsdom below): create a `uPlot` instance into a container `div`, sized to the cell via a **ResizeObserver/fit** (JS px sizing — the WebView2 `%`-height collapse means the cell reports 0 height; measure the nearest real-height ancestor and `u.setSize({width,height})`).
- On data change (`xs.length` as the live trigger — the store mutates arrays in place, same lesson as the map track): `u.setData(toUPlotData(xs, ys))` and set the x-scale to `scrollWindow(xs, windowMs)` for the scrolling window. y auto-ranges.
- Keep the value overlay (`fmtNum(value)` + unit) and the name; the scrubber/zoom: `zoom` shrinks the window via `scrollWindow`. Hover uses uPlot's native cursor (drop the custom SVG crosshair/tooltip, or keep a minimal value readout). `scalesOn` toggles the axes/grid visibility.
- Cleanup `u.destroy()` + disconnect the observer on unmount/toggle-away.
- Import uPlot CSS (`uplot/dist/uPlot.min.css`).

### 4. jsdom / test guard
uPlot needs a canvas 2D context + real size, which jsdom lacks → **do not construct uPlot in unit tests** (guard on canvas-context availability or a zero-size container, mirroring the map's WebGL guard). The component still renders its container + value overlay in jsdom (so the existing `linechart` testid + value tests pass); the chart itself is build/live/Playwright-verified. Pure data-prep + options builder carry the unit coverage.

### 5. Grid / cell integration (unchanged contract)
`WidgetGrid` keeps rendering `<LineChart .../>` in the line cells with the same props; the header GAUGE-toggle/× and the resize grip are unchanged. The widget-drag-via-header (Phase 8 fix) means dragging the chart body won't move the widget — fine (uPlot may use drag for zoom; acceptable). The gauge↔line toggle still works (line→gauge/back).

## Testing

- **Unit (vitest):** `toUPlotData` (ms→s, alignment, empty), `scrollWindow` (window math, zoom shrink, empty default), `lineOpts` (time x-axis, y auto, stroke=accent, axes present). The `LineChart` component renders the container + value overlay in jsdom WITHOUT constructing uPlot (guard) — assert `data-testid="linechart"` + value text present, no crash.
- **Playwright:** uPlot renders on canvas in Chromium → the widget-grid baseline (`?mock=1`) will change (line cells now show a uPlot chart) → clean-regenerate the affected baselines and verify the chart renders (axes + line), not blank.
- **Live verify:** line widgets show a scrolling time x-axis; the signal trends are visible against a stable y-axis; resize/toggle/hover work; zoom shrinks the window.

## Conventions / decisions (locked)

- **uPlot** (new dep) for line widgets; the SVG `linePath.ts`/`lineViz` may be removed or left unused (the gauge keeps its SVG). Keep the `LineChart` prop contract so the grid is untouched.
- **Real time x-axis + scrolling window** (last `windowMs/zoom` seconds), **stable auto-ranged y**.
- uPlot must be **JS-sized** (ResizeObserver/fit) — the WebView2 `%`-height collapse from the map applies; do not rely on CSS `%` height.
- uPlot is **not constructed in jsdom** (no canvas) → pure helpers + Playwright + live carry chart coverage; the component still renders the testid + value overlay headless.
- The `.NET` stack already uses ScottPlot for its strip charts — matching is **out of scope** here (per-stack idiomatic).

## Risks / notes

- The WebView2 sizing collapse is the top risk — reuse the map's JS-fit pattern (measure nearest non-zero-height ancestor, `setSize`, ResizeObserver + late re-fit).
- uPlot bundle (~50 KB) is small; CSS import needed.
- Removing the SVG line path may orphan `linePath.ts`/`hoverInfo.ts`/`lineViz` tests — delete or keep them (they're pure + harmless); decide in the plan.
- e2e baselines for the widget grid + interactions WILL change (line cells render uPlot) — clean-regen + verify.

## Out of scope

- Multi-series charts, legends beyond a value readout, axis unit labels beyond the value overlay.
- Replacing the gauge or map widgets (SVG gauge + MapLibre map stay).
- .NET chart changes.
