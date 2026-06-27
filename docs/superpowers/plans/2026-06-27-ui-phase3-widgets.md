# INU-MONITOR Rewrite — Phase 3: Widget Grid

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fill the OVERVIEW dashboard column (`overview-dash`) with the design's live widgets — radial **gauges**, **SVG line charts**, and a compact **GPS map** — in a fixed default layout. Interactions (drag/resize/add/remove/toggle/zoom) come in Phase 4; this phase is static layout + live rendering.

**Architecture:** Pure widget math (gauge auto-scale + needle, line-path scaling, track projection) is vitest-tested; the SVG components (`Gauge`, `LineChart`, `MapWidget`) are jsdom-smoke-tested and visually pinned via Playwright; a `defaultWidgets` layout function + `WidgetGrid` compose them and replace the `overview-dash` placeholder. All driven by the live store (and the `?mock=1` fixture for E2E).

**Tech Stack:** React 18 + TS strict, vitest, Playwright (existing). Pure SVG (no chart lib) — faithful to the sample and to the Phase-4 zoom/hover.

## Global Constraints

- Design source: `docs/sample ui/INU Monitor (standalone-src).html` — the gauge SVG (lines ~214-223), the line SVG (~225-238), the map SVG (~169-200), and the `computeWidgets` math (lines ~573-601). Port the math faithfully.
- Use real data: gauge value = `store.latest(id)`; line series = `store.series(id).arrays()` (ms→sec x); map track = `store.gpsTrack()`. Theme via the existing CSS vars.
- TS strict, no `any`; vitest node/jsdom split intact; React 19 type-only React; CSS theme vars (color-mix for translucency).
- Mount in the existing `overview-dash` element (Phase 2 placeholder). The grid is a CSS grid of fixed-size cells (the sample uses 158px cells, gap 10) — a **static** default layout; no drag/resize yet.

## Gauge math (port from the sample `computeWidgets`)

Given a value `v`: `raw = max(|v|*1.3, 1e-6)`, `ex = floor(log10(raw))`, `ff = raw/10^ex`, `nf = ff<=1?1: ff<=2?2: ff<=2.5?2.5: ff<=5?5:10`, `R = nf*10^ex`. `frac = clamp((v+R)/(2R), 0,1)`, `ang = (-135 + frac*270)°`. Needle tip (in an 80×80 viewBox, center 40,40, len 28): `nx = 40 + 28*sin(ang_rad)`, `ny = 40 - 28*cos(ang_rad)`. Scale labels: `gMin=-R, gQ1=-R/2, 0, gQ3=R/2, gMax=R` via `fmtScale` (a>=100→toFixed(0), >=1→toFixed(1), >=0.1→toFixed(2), else toFixed(3); strip trailing zeros). Value text via `fmt` (a>=100→toFixed(1), >=1→toFixed(3), else toFixed(6)).

---

### Task 1: Gauge math

**Files:** Create `rust/src/ui/app/widgets/gaugeViz.ts` + `gaugeViz.test.ts`

**Interfaces:**
- `fmtNum(v: number): string` (the `fmt` above)
- `fmtScale(v: number): string`
- `gaugeViz(value: number): { angleDeg: number; nx: number; ny: number; gMin: string; gQ1: string; gQ3: string; gMax: string; valueText: string }`

- [ ] **Step 1: Failing test**

```ts
// gaugeViz.test.ts
import { describe, it, expect } from "vitest";
import { gaugeViz, fmtScale, fmtNum } from "./gaugeViz";

describe("gaugeViz", () => {
  it("centers the needle at value 0 (angle -? ... mid)", () => {
    const g = gaugeViz(0);
    // v=0 -> frac=0.5 -> ang = -135 + 0.5*270 = 0 deg -> needle straight up
    expect(g.angleDeg).toBeCloseTo(0, 5);
    expect(g.nx).toBeCloseTo(40, 3);
    expect(g.ny).toBeCloseTo(12, 3); // 40 - 28*cos(0)
  });
  it("computes a nice round scale and value text", () => {
    const g = gaugeViz(19.648);
    expect(g.valueText).toBe("19.648");
    expect(g.gMax).toBe("40");   // raw=25.5 -> ex=1 ff=2.55 -> nf=5 -> R=50? verify in impl
  });
  it("fmtScale strips trailing zeros", () => {
    expect(fmtScale(50)).toBe("50");
    expect(fmtScale(2.5)).toBe("2.5");
    expect(fmtScale(0)).toBe("0");
  });
});
```
(Adjust the `gMax` expectation to whatever the ported math yields — compute it in the impl and set the test to match the real value; the goal is the math is the sample's, not a specific magic number.)

- [ ] **Step 2-5:** Run FAIL → implement `gaugeViz.ts` (port the math above; `angleDeg = -135 + frac*270`; needle uses radians) → PASS → tsc → commit `feat(rust-ui): gauge auto-scale + needle math`.

---

### Task 2: Gauge component

**Files:** Create `rust/src/ui/app/widgets/Gauge.tsx` + `Gauge.test.tsx`; add gauge CSS to `theme.css`.

**Interfaces:** `Gauge({ name, value, unit, scalesOn }: { name: string; value: number; unit: string; scalesOn: boolean }): React.JSX.Element` — SVG (80×80 viewBox): face circle, arc path, tick marks (shown when `scalesOn`), needle `<line>` to `gaugeViz(value).nx/ny`, scale labels (when `scalesOn`), and the value + unit text. `data-testid="gauge"`.

- [ ] jsdom smoke: renders an `<svg>`, the needle line endpoint matches `gaugeViz(value)` (assert the `<line x2>` ≈ nx), value text present. Build-verify the SVG. Commit.

---

### Task 3: Line-chart path math

**Files:** Create `rust/src/ui/app/widgets/linePath.ts` + `linePath.test.ts`

**Interfaces:** `lineViz(xs: number[], ys: number[], viewW = 200, viewH = 80): { path: string; yHi: string; yMid: string; yLo: string; xs0: string; xMid: string; xLast: string }` — auto-ranges `ys` to `[6, viewH-6]` (inverted, like the sample), maps `xs` index to `[0, viewW]`, builds an `M…L…` path; y labels from the ys range (`fmtNum`), x labels from the xs time window (`xs` are seconds; `-Ns / -N/2s / 0s`). Empty series → empty path + dashes.

- [ ] Failing test (path starts `M`, has N points, y/x labels, empty-series guard) → implement → tsc → commit.

---

### Task 4: LineChart component

**Files:** Create `rust/src/ui/app/widgets/LineChart.tsx` + `LineChart.test.tsx`; CSS.

**Interfaces:** `LineChart({ name, xs, ys, unit, value, scalesOn }): React.JSX.Element` — SVG (200×80 viewBox, `preserveAspectRatio:none`): grid pattern, mid dashed line, the `lineViz(xs,ys).path` stroked in accent, value top-right, y/x axis labels (when `scalesOn`). `data-testid="linechart"`. jsdom smoke: renders svg + path `d` starts with `M` (when data) + value text. Commit.

---

### Task 5: Map projection math

**Files:** Create `rust/src/ui/app/widgets/mapProj.ts` + `mapProj.test.ts`

**Interfaces:** `projectTrack(lat: number[], lon: number[], view = { x: 60, y: 150, w: 470, h: 340 }): { path: string; last: { x: number; y: number } | null }` — projects lat/lon into the SVG box (lon→x, lat→y inverted) using the data bbox (with a small pad); builds the track path; returns the last point for the marker. Empty → empty path + null.

- [ ] Failing test (2 points → path `M…L…`, last point within the view box, empty guard) → implement → tsc → commit.

---

### Task 6: MapWidget component

**Files:** Create `rust/src/ui/app/widgets/MapWidget.tsx` + `MapWidget.test.tsx`; CSS.

**Interfaces:** `MapWidget({ lat, lon }: { lat: number[]; lon: number[] }): React.JSX.Element` — SVG with a grid pattern, range rings, the projected track path (`projectTrack`) in accent, a marker at the last point, axis ticks. `data-testid="mapwidget"`. (OSM/Leaflet toggle is Phase 5/7 — here it's the SVG track only.) jsdom smoke: renders svg + a `path`. Commit.

---

### Task 7: Default widget layout

**Files:** Create `rust/src/ui/app/widgets/layout.ts` + `layout.test.ts`

**Interfaces:**
- `type Widget = { id: string; kind: "gauge" | "line" | "map"; channelId?: number; name: string; cols: number; rows: number }`
- `defaultWidgets(channels: ChannelMeta[]): Widget[]` — a fixed default set: one `map` (4×4) if lat/lon present; a `gauge` (1×1) for each `widget === "gauge"` channel; a `line` (2×1) for a few key strip channels (e.g. `roll`, `acc_z`). Stable order. Each gets a deterministic `id`.

- [ ] Failing test (given the mock-like channel set → includes a map, the gauge channels as gauges, ≥1 line; ids unique) → implement → tsc → commit.

---

### Task 8: WidgetGrid + mount in OverviewView

**Files:** Create `rust/src/ui/app/widgets/WidgetGrid.tsx` + `WidgetGrid.test.tsx`; modify `OverviewView.tsx`; CSS.

**Interfaces:** `WidgetGrid({ store, scalesOn }: { store: TelemetryStore; scalesOn: boolean }): React.JSX.Element` — computes `defaultWidgets(store.channels())`, renders each cell (a framed widget with a small header showing the name) containing the right component: `gauge`→`Gauge` (value=`store.latest`), `line`→`LineChart` (series=`store.series(id).arrays()`), `map`→`MapWidget` (`store.gpsTrack()`). CSS grid of fixed cells (158px, gap 10) with `grid-column/row span` from `cols/rows`. `OverviewView` renders `<WidgetGrid store={store} scalesOn={true}/>` inside `overview-dash` (replace the placeholder text; keep the `overview-dash` testid).

- [ ] jsdom smoke: renders ≥1 gauge + ≥1 line + the map (by testid); `overview-dash` still present. Build-verify. Commit.

---

### Task 9: Playwright — widget grid baseline

**Files:** Create `rust/e2e/widgets.spec.ts`

- [ ] Spec: `goto("/?mock=1")`; assert `getByTestId("mapwidget")`, at least one `getByTestId("gauge")`, one `getByTestId("linechart")` are visible; `await document.fonts.ready`; `await expect(page.getByTestId("overview-dash")).toHaveScreenshot("widgets.png")` (screenshot just the dashboard column for stability). Generate the baseline with a CLEAN regen: stop any dev server, `rm -rf node_modules/.vite`, `npm run e2e:update`, then `npm run e2e` green. READ the baseline PNG to confirm gauges + a line + the map track render (not blank). Commit the spec + baseline.

---

### Task 10: build + live verify

- [ ] `npx tsc --noEmit && npm test && npm run build && npm run e2e` (all green) + `cd src-tauri && cargo test`. Launch `RIDE_DB=../../data/ride_small.db RIDE_SPEED=5 npm run tauri dev`; confirm the OVERVIEW right column now shows live gauges (needles moving), scrolling line charts, and the GPS map track — compare to `docs/sample ui/screenshots/overview-full.png`. Commit any visual tweaks.

---

## Self-Review

**Spec coverage:** gauges (Tasks 1-2), line charts (3-4), map widget (5-6), default layout + grid mounting (7-8), E2E + visual baseline (9), live verify (10). Interactions deferred to Phase 4 (stated). ✓
**Placeholder scan:** No TBD; the OSM/Leaflet map toggle is explicitly deferred to Phase 5/7 (Task 6 renders the SVG track only). ✓
**Type consistency:** `gaugeViz` (1)→`Gauge` (2); `lineViz` (3)→`LineChart` (4); `projectTrack` (5)→`MapWidget` (6); `defaultWidgets`/`Widget` (7)→`WidgetGrid` (8)→`OverviewView`. All read existing store selectors. ✓

> **Note for Phase 4:** `WidgetGrid` + `defaultWidgets` become the editable widget model — drag-to-add (param→gauge), drag-reorder, pointer-resize (cols/rows), gauge↔line toggle, remove, line context-menu zoom + hover tooltip. The `Widget` type gains `col/row` positions and the grid becomes a dropzone. Add per-interaction Playwright tests.
