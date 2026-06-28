# UI Phase 5 — Map Widget (OSM toggle + geo chrome + resize) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the flight-track map a full map widget: an OSM/Leaflet basemap toggle, geographic chrome (live coordinates, compass, scale bar) over the SVG track, and a resizable map cell.

**Architecture:** The SVG `MapWidget` gains absolutely-positioned overlays (coords/compass/scale) and an `osm` toggle. When OSM is on, a Leaflet map (ported from the orphaned pre-rewrite `rust/src/ui/GpsMap.tsx`) is mounted into an overlay div on top of the SVG, drawing the GPS polyline + live marker over real OSM tiles. Map resize is enabled by extending `widgetModel.setSize` to a map branch and rendering the resize grip on map cells in `WidgetGrid`.

**Tech Stack:** React 19 + TS strict, Leaflet 1.9 (already a dependency: `leaflet` + `@types/leaflet`), vitest (node + jsdom), Playwright. Leaflet/tiles are **build-and-live-verified**, not unit-tested (per repo convention for chart/map/GUI code); pure logic carries the test coverage.

## Global Constraints

- TS strict, **no `any`**. React 19: `import type React`; `useId()` for SVG def ids.
- **Theme CSS vars only** — no hardcoded hex in components or added CSS. Palette in `rust/src/ui/app/theme.css` `:root`.
- Data layer reused, not modified. Map reads `store.gpsTrack(): { lat: number[]; lon: number[] }` (already wired through `WidgetGrid`).
- Leaflet is GUI-only: its effect must **no-op in jsdom** (guard on a zero-size container) so unit tests never instantiate a real map. Default state is OSM **off** (SVG mode), so tests render the SVG path.
- Behavioral/visual parity with the sample (`docs/sample ui/INU Monitor (standalone-src).html`): OSM toggle label `OSM MAP`↔`GRID VIEW` (lines 167, 656-658); compass `N↑` top-left (line 198); scale bar bottom-right (line 199); coords readout (line 167); Leaflet init/tiles/polyline/markers (lines 429-447 + `GpsMap.tsx`).
- Keep the existing SVG track/rings/axis/marker (Phase 3 `MapWidget`) intact — this phase **adds** chrome + OSM, it does not replace the SVG.

## File Structure

- `rust/src/ui/app/widgets/geoFormat.ts` (new) — pure coordinate/scale formatting.
- `rust/src/ui/app/widgets/MapWidget.tsx` (modify) — coords/compass/scale overlays + OSM toggle + Leaflet lifecycle.
- `rust/src/ui/app/widgets/widgetModel.ts` (modify) — `setSize` map branch (resizable).
- `rust/src/ui/app/widgets/WidgetGrid.tsx` (modify) — render the resize grip on map cells.
- `rust/src/ui/app/theme.css` (modify) — `.mapwidget-*` overlay + OSM + toggle CSS (theme vars).
- `rust/e2e/map.spec.ts` (new) — map chrome + toggle present; SVG-mode baseline.
- Test files alongside new/changed modules. Delete the orphaned `rust/src/ui/GpsMap.tsx` after porting (it is dead since the rewrite — confirm no importers first).

---

### Task 1: Allow map resize in the widget model

**Files:**
- Modify: `rust/src/ui/app/widgets/widgetModel.ts` (the `setSize` function)
- Test: `rust/src/ui/app/widgets/widgetModel.test.ts` (extend)

**Interfaces:**
- Consumes/Produces: `setSize(ws: LayoutWidget[], id: string, cols: number, rows: number): LayoutWidget[]` — unchanged signature. New behavior: a `kind === "map"` branch clamps `cols ∈ [2,8]`, `rows ∈ [2,6]` (free aspect, not square). Gauge (square) and line branches unchanged.

- [ ] **Step 1: Write the failing test (extend)**

```ts
// add to widgetModel.test.ts
describe("setSize map", () => {
  it("clamps a map to cols[2,8]/rows[2,6], free aspect", () => {
    const m = (over = {}) => ({ id: "m", kind: "map" as const, name: "Map", unit: "", cols: 4, rows: 4, col: 1, row: 1, zoom: 1, ...over });
    expect(setSize([m()], "m", 99, 99)[0]).toMatchObject({ cols: 8, rows: 6 });
    expect(setSize([m()], "m", 1, 1)[0]).toMatchObject({ cols: 2, rows: 2 });
    expect(setSize([m()], "m", 5, 3)[0]).toMatchObject({ cols: 5, rows: 3 }); // not forced square
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/widgetModel.test.ts`
Expected: FAIL (map currently falls into the line branch → cols clamp [1,6]/rows [1,4], so cols 99→6 not 8).

- [ ] **Step 3: Implement** — in `setSize`, add a map branch BEFORE the gauge/line logic:

```ts
export function setSize(ws: LayoutWidget[], id: string, cols: number, rows: number): LayoutWidget[] {
  return ws.map((w) => {
    if (w.id !== id) return w;
    if (w.kind === "map") {
      return { ...w, cols: clamp(cols, 2, 8), rows: clamp(rows, 2, 6) };
    }
    if (w.kind === "gauge") {
      const s = clamp(Math.max(cols, rows), 1, GAUGE_MAX);
      return { ...w, cols: s, rows: s };
    }
    return { ...w, cols: clamp(cols, 1, LINE_MAX_COLS), rows: clamp(rows, 1, LINE_MAX_ROWS) };
  });
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/widgetModel.test.ts`
Expected: PASS (new + existing).

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/widgetModel.ts rust/src/ui/app/widgets/widgetModel.test.ts
git commit -m "feat(rust-ui): map widgets are resizable (setSize map branch)"
```

---

### Task 2: Render the resize grip on map cells

**Files:**
- Modify: `rust/src/ui/app/widgets/WidgetGrid.tsx`
- Test: `rust/src/ui/app/widgets/WidgetGrid.test.tsx` (extend)

**Interfaces:** No signature change. WidgetGrid currently renders the resize handle (`.widget-cell-resize` with `onPointerDown={startResize(w)}`) only for non-map widgets. Change the condition so **map cells also get the resize handle**. Map cells still have NO toggle and NO remove (only the resize grip + drag). `startResize` already calls `wm.setSize`, which now supports map (Task 1).

- [ ] **Step 1: Write the failing test (extend)**

```tsx
// @vitest-environment jsdom
// add to WidgetGrid.test.tsx (reuse the populated-store render setup)
it("map cells have a resize handle but no toggle/remove", () => {
  const mapCell = document.querySelector('[data-widget] [data-testid="mapwidget"]')!.closest("[data-widget]") as HTMLElement;
  expect(mapCell.querySelector(".widget-cell-resize")).not.toBeNull();
  expect(mapCell.querySelector('[data-act="toggle"]')).toBeNull();
  expect(mapCell.querySelector('[data-act="remove"]')).toBeNull();
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/WidgetGrid.test.tsx`
Expected: FAIL (map has no `.widget-cell-resize`).

- [ ] **Step 3: Implement** — in `WidgetGrid.tsx`, the resize-handle block is currently gated `w.kind !== "map"`. Change it to render for ALL kinds (the handle does not depend on kind):

```tsx
{/* resize handle — all widget kinds (gauge square / line / map free) */}
<div className="widget-cell-resize" onPointerDown={startResize(w)} aria-label="resize">
  <svg viewBox="0 0 10 10" width="9" height="9"><path d="M9.5 1 L9.5 9.5 L1 9.5" className="widget-cell-resize-glyph" /></svg>
</div>
```

(Leave the header logic unchanged: toggle/remove still only for `w.kind !== "map"`.)

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/WidgetGrid.test.tsx && npx tsc --noEmit`
Expected: PASS; tsc clean.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/WidgetGrid.tsx rust/src/ui/app/widgets/WidgetGrid.test.tsx
git commit -m "feat(rust-ui): map cells get a resize grip"
```

---

### Task 3: Geo coordinate + scale formatting (pure)

**Files:**
- Create: `rust/src/ui/app/widgets/geoFormat.ts`
- Test: `rust/src/ui/app/widgets/geoFormat.test.ts`

**Interfaces:**
- Produces:
  - `fmtCoord(lat: number[], lon: number[]): string` — formats the LAST point as `"32.0853°N 34.7818°E"` (4 decimals; N/S from lat sign, E/W from lon sign; absolute value printed). Empty arrays → `"—"`.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from "vitest";
import { fmtCoord } from "./geoFormat";

describe("fmtCoord", () => {
  it("formats the last point with hemisphere letters", () => {
    expect(fmtCoord([32.0853, 32.1], [34.7818, 34.9])).toBe("32.1000°N 34.9000°E");
  });
  it("uses S/W for negative lat/lon", () => {
    expect(fmtCoord([-12.5], [-77.0])).toBe("12.5000°S 77.0000°W");
  });
  it("empty → em dash", () => {
    expect(fmtCoord([], [])).toBe("—");
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/geoFormat.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement `geoFormat.ts`**

```ts
export function fmtCoord(lat: number[], lon: number[]): string {
  if (lat.length === 0 || lon.length === 0) return "—";
  const la = lat[lat.length - 1];
  const lo = lon[lon.length - 1];
  const ns = la >= 0 ? "N" : "S";
  const ew = lo >= 0 ? "E" : "W";
  return `${Math.abs(la).toFixed(4)}°${ns} ${Math.abs(lo).toFixed(4)}°${ew}`;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/geoFormat.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/geoFormat.ts rust/src/ui/app/widgets/geoFormat.test.ts
git commit -m "feat(rust-ui): geo coordinate formatting"
```

---

### Task 4: MapWidget SVG chrome — coords + compass + scale

**Files:**
- Modify: `rust/src/ui/app/widgets/MapWidget.tsx`
- Modify: `rust/src/ui/app/widgets/theme.css` → actually `rust/src/ui/app/theme.css`
- Test: `rust/src/ui/app/widgets/MapWidget.test.tsx` (extend)

**Interfaces:** `MapWidget({ lat, lon }: { lat: number[]; lon: number[] })` — unchanged props. Adds, OVER the existing SVG (the container is already `position:relative`): a compass `N↑` (top-left), a coords readout via `fmtCoord(lat, lon)` (top-right), and a scale bar (bottom-right: a short line + `2 km`). All as absolutely-positioned overlays. Keep the SVG track/rings/marker.

- [ ] **Step 1: Write the failing test (extend)**

```tsx
// @vitest-environment jsdom
// add to MapWidget.test.tsx
it("renders compass, coords readout, and scale bar", () => {
  // render <MapWidget lat={[32.0853]} lon={[34.7818]} /> (reuse existing render helper)
  expect(screen.getByText("N↑")).toBeTruthy();
  expect(screen.getByText("32.0853°N 34.7818°E")).toBeTruthy();
  expect(screen.getByText("2 km")).toBeTruthy();
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/MapWidget.test.tsx`
Expected: FAIL (overlays absent).

- [ ] **Step 3: Implement** — in `MapWidget.tsx`, import `fmtCoord` from `./geoFormat`, and add the overlays inside the `.mapwidget-container` div, after the `</svg>`:

```tsx
{/* geographic chrome overlays */}
<div className="mapwidget-compass">N↑</div>
<div className="mapwidget-coords">{fmtCoord(lat, lon)}</div>
<div className="mapwidget-scale"><span className="mapwidget-scale-bar" />2 km</div>
```

CSS (theme vars) in `theme.css`:

```css
.mapwidget-compass{position:absolute;left:8px;top:6px;font:500 9px/1 var(--mono);color:var(--dim);pointer-events:none}
.mapwidget-coords{position:absolute;right:8px;top:6px;font:500 9px/1 var(--mono);color:var(--dim);pointer-events:none}
.mapwidget-scale{position:absolute;right:8px;bottom:6px;display:flex;align-items:center;gap:6px;font:500 9px/1 var(--mono);color:var(--dim);pointer-events:none}
.mapwidget-scale-bar{width:20px;height:1px;background:var(--dim);display:inline-block}
```

(Confirm `--mono` and `--dim` exist in `:root`.)

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/MapWidget.test.tsx && npx tsc --noEmit && npm run build`
Expected: PASS; tsc clean; build OK.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/MapWidget.tsx rust/src/ui/app/theme.css rust/src/ui/app/widgets/MapWidget.test.tsx
git commit -m "feat(rust-ui): map SVG chrome (compass, coords, scale bar)"
```

---

### Task 5: MapWidget OSM/Leaflet toggle

**Files:**
- Modify: `rust/src/ui/app/widgets/MapWidget.tsx`
- Modify: `rust/src/ui/app/theme.css`
- Test: `rust/src/ui/app/widgets/MapWidget.test.tsx` (extend)
- Delete (after confirming no importers): `rust/src/ui/GpsMap.tsx`

**Interfaces:** `MapWidget` gains internal `osm` state (default `false`). A toggle button (label `OSM MAP` when off, `GRID VIEW` when on) sits top-right of the map header area. When `osm`, a Leaflet map is mounted into an overlay div (`position:absolute;inset:0;z-index:3`) over the SVG; it draws the GPS polyline + a live marker over OSM tiles, panning to the latest point. The Leaflet effect **no-ops in jsdom** (guard: bail if the container has zero size), so unit tests never instantiate a real map.

- [ ] **Step 1: Write the failing test (extend)**

```tsx
// @vitest-environment jsdom
import { fireEvent } from "@testing-library/react";
it("toggles the OSM button label and shows the leaflet overlay container", () => {
  // render <MapWidget lat={[32.08]} lon={[34.78]} />
  const btn = screen.getByText("OSM MAP");
  fireEvent.click(btn);
  expect(screen.getByText("GRID VIEW")).toBeTruthy();
  expect(document.querySelector(".mapwidget-osm")).not.toBeNull(); // overlay div present (leaflet itself no-ops in jsdom)
  fireEvent.click(screen.getByText("GRID VIEW"));
  expect(screen.getByText("OSM MAP")).toBeTruthy();
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/MapWidget.test.tsx`
Expected: FAIL (no toggle).

- [ ] **Step 3: Implement** — extend `MapWidget.tsx`. Reference `rust/src/ui/GpsMap.tsx` for the Leaflet lifecycle (it uses `circleMarker`/`polyline`, avoiding Leaflet's missing-default-icon problem):

```tsx
import type React from "react";
import { useId, useState, useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { projectTrack } from "./mapProj";
import { fmtCoord } from "./geoFormat";

// …existing VIEW/CX/CY/TICK consts…

export function MapWidget({ lat, lon }: MapWidgetProps): React.JSX.Element {
  const gridId = useId();
  const { path, last } = projectTrack(lat, lon);
  const [osm, setOsm] = useState(false);
  const elRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const lineRef = useRef<L.Polyline | null>(null);
  const markerRef = useRef<L.CircleMarker | null>(null);

  // Mount/teardown Leaflet only while OSM is on AND the container has real size
  // (jsdom reports 0 → unit tests never instantiate a real map).
  useEffect(() => {
    if (!osm) return;
    const el = elRef.current;
    if (!el || el.clientWidth === 0 || el.clientHeight === 0) return;
    const map = L.map(el, { zoomControl: true, attributionControl: true }).setView([32.0853, 34.7818], 11);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { attribution: "© OpenStreetMap contributors", maxZoom: 19 }).addTo(map);
    lineRef.current = L.polyline([], { color: "#38c5e0", weight: 2.5, opacity: 0.95 }).addTo(map);
    markerRef.current = L.circleMarker([32.0853, 34.7818], { radius: 6, color: "#0a0e14", weight: 2, fillColor: "#ffffff", fillOpacity: 1 }).addTo(map);
    mapRef.current = map;
    setTimeout(() => map.invalidateSize(), 60);
    return () => { map.remove(); mapRef.current = null; lineRef.current = null; markerRef.current = null; };
  }, [osm]);

  // Update the polyline + marker as the track grows
  useEffect(() => {
    const line = lineRef.current, marker = markerRef.current, map = mapRef.current;
    if (!line || !marker || !map || lat.length === 0) return;
    const pts: [number, number][] = lat.map((la, i) => [la, lon[i]]);
    line.setLatLngs(pts);
    const lastPt = pts[pts.length - 1];
    marker.setLatLng(lastPt);
    map.panTo(lastPt, { animate: false });
  }, [lat, lon, osm]);

  return (
    <div data-testid="mapwidget" className="mapwidget-container">
      <button className="mapwidget-osm-toggle" onClick={() => setOsm((v) => !v)}>{osm ? "GRID VIEW" : "OSM MAP"}</button>
      {osm && <div ref={elRef} className="mapwidget-osm" />}
      <svg /* …existing SVG unchanged… */ >…</svg>
      <div className="mapwidget-compass">N↑</div>
      <div className="mapwidget-coords">{fmtCoord(lat, lon)}</div>
      <div className="mapwidget-scale"><span className="mapwidget-scale-bar" />2 km</div>
    </div>
  );
}
```

CSS (theme vars; the OSM toggle mirrors the sample's cyan pill):

```css
.mapwidget-osm{position:absolute;inset:0;z-index:3}
.mapwidget-osm-toggle{position:absolute;right:8px;top:22px;z-index:4;cursor:pointer;padding:3px 9px;border:1px solid var(--border2);border-radius:5px;background:var(--panel2);color:var(--accent);font:600 9px/1 var(--mono);letter-spacing:.06em;user-select:none}
```

Notes for the implementer:
- The OSM overlay sits at `z-index:3` over the SVG; the toggle at `z-index:4` stays clickable. The coords/compass/scale overlays may be hidden under the OSM layer when on — acceptable (OSM tiles carry their own labels). Keep them rendered.
- Leaflet's polyline/marker colors are passed in JS (Leaflet options, not CSS) — the literal hex there is a Leaflet API argument, not component CSS; that's acceptable (same as the old `GpsMap.tsx`). Do NOT introduce hardcoded hex in the component's own CSS classes.
- After implementing, confirm nothing imports `rust/src/ui/GpsMap.tsx` (`grep -rn "GpsMap" rust/src`), then delete it (dead since the rewrite). If anything still imports it, leave it and note in the report.

- [ ] **Step 2 → 4: TDD + verify**

Run: `cd rust && npx vitest run src/ui/app/widgets/MapWidget.test.tsx` (FAIL → implement → PASS); then full `npm test` green; `npx tsc --noEmit` clean; `npm run build` succeeds (Leaflet bundles).

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/MapWidget.tsx rust/src/ui/app/theme.css rust/src/ui/app/widgets/MapWidget.test.tsx
git rm rust/src/ui/GpsMap.tsx   # only if no importers
git commit -m "feat(rust-ui): map OSM/Leaflet toggle; remove dead GpsMap"
```

---

### Task 6: Playwright — map chrome + toggle baseline

**Files:**
- Create: `rust/e2e/map.spec.ts`

**Interfaces:** uses `?mock=1`. The mock GPS gives a track, so the map renders the SVG track + chrome. Do NOT screenshot OSM-on (live tiles are non-deterministic + need network); assert the toggle button exists and screenshot the SVG (GRID) mode.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect } from "@playwright/test";
test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("map shows geo chrome + an OSM toggle", async ({ page }) => {
  const map = page.getByTestId("mapwidget");
  await expect(map).toBeVisible();
  await expect(map.getByText("N↑")).toBeVisible();
  await expect(map.getByText(/°N .*°E/)).toBeVisible();
  await expect(map.getByText("2 km")).toBeVisible();
  await expect(map.getByText("OSM MAP")).toBeVisible();
});

test("map (grid mode) matches the visual baseline", async ({ page }) => {
  await page.evaluate(() => document.fonts.ready);
  await expect(page.getByTestId("mapwidget")).toHaveScreenshot("map.png");
});
```

- [ ] **Step 2: Clean baseline regen** (avoid the Vite-cache trap)

```bash
cd rust
# stop anything on :1420
rm -rf node_modules/.vite
npm run e2e:update
npm run e2e   # all green
```

- [ ] **Step 3: Verify the baseline** — open `rust/e2e/map.spec.ts-snapshots/map-chromium-win32.png`; confirm the SVG track + rings + compass `N↑` + coords + `2 km` + `OSM MAP` button render. If blank/wrong, STOP.

- [ ] **Step 4: Commit**

```bash
git add rust/e2e/map.spec.ts rust/e2e/map.spec.ts-snapshots
git commit -m "test(rust-ui): map chrome + toggle E2E + baseline"
```

---

### Task 7: Full gate + live verify

- [ ] **Step 1: Run the whole suite**

```bash
cd rust && npx tsc --noEmit && npm test && npm run build && npm run e2e
cd src-tauri && cargo test
```
Expected: all green.

- [ ] **Step 2: Live verify** (GUI — cannot run headless)

```bash
cd rust && RIDE_DB=../../data/ride_small.db RIDE_SPEED=2 npm run tauri dev
```
Confirm on OVERVIEW: the map shows compass/coords/scale; clicking **OSM MAP** swaps to a live OpenStreetMap basemap with the GPS polyline + marker over real tiles (and real place labels); clicking **GRID VIEW** returns to the SVG; the map cell **resizes** via the bottom-right grip. (OSM needs network for tiles.) Compare to `docs/sample ui/screenshots/`.

- [ ] **Step 3: Commit any visual tweaks**, then finish the branch (PR).

---

## Self-Review

**Spec coverage:** map resize (T1 model + T2 grip), coords/compass/scale chrome (T3 fmtCoord + T4 overlays), OSM/Leaflet toggle (T5), E2E + baseline (T6), gate + live (T7). ✓
**Placeholder scan:** No TBD/TODO; Leaflet lifecycle is fully shown (ported from `GpsMap.tsx`); the SVG body in T5 is "…existing SVG unchanged…" because T4/Phase-3 already define it verbatim — the implementer keeps the current `<svg>` block as-is. ✓
**Type consistency:** `fmtCoord(lat, lon)` (T3) consumed by T4/T5 MapWidget; `setSize` map branch (T1) consumed by T2's resize grip via `startResize`→`wm.setSize`; Leaflet refs typed via `@types/leaflet`. ✓
**jsdom safety:** Leaflet effect guards on zero container size, so unit tests (OSM default off; even when toggled, container is 0-size in jsdom) never instantiate a real map. ✓
