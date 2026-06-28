# UI Phase 9 — uPlot Time-Series Line Widget Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the SVG line widget with a uPlot time-series chart — a real time x-axis with a scrolling window and a stable auto-ranged y-axis — keeping the same `LineChart` prop contract so the editable grid is unchanged.

**Architecture:** Pure helpers (`uplotData`, `uplotOpts`) build uPlot's data + options; `LineChart` is rewritten to mount a uPlot instance (guarded off in jsdom — no canvas), JS-sized to the cell (the WebView2 `%`-height collapse the map hit applies), `setData` on each frame (triggered by `xs.length`), with the x-scale held to a scrolling window. The cell header (gauge↔line toggle, ×), resize, and value overlay are unchanged.

**Tech Stack:** uPlot (new dep), React 19 + TS strict, vitest, Playwright.

## Global Constraints

- TS strict, **no `any`**. React 19 `import type React`. Theme CSS vars only in component CSS; uPlot's JS style config (colors) is data — hardcoded hex acceptable there (like the map style), using the INU palette values.
- **uPlot is NOT constructed in jsdom** (no canvas/size) — guard on canvas-2d availability or zero size, mirroring the map's WebGL guard. The component still renders its container + value overlay headless so existing `linechart` testid/value tests pass.
- **uPlot must be JS-sized** (measure nearest non-zero-height ancestor + `u.setSize` + ResizeObserver + late re-fit) — do NOT rely on CSS `%` height (WebView2 collapses it).
- **Live trigger is `xs.length`** in the update effect deps (the store mutates the same arrays in place — same lesson as the map track).
- Keep the `LineChart` prop contract: `{ name, xs, ys, unit, value, scalesOn, zoom?, onZoomBy?, onResetZoom? }`, `data-testid="linechart"`.

## File Structure

- `rust/src/ui/app/widgets/uplotData.ts` (new) — `toUPlotData`, `scrollWindow` (pure).
- `rust/src/ui/app/widgets/uplotOpts.ts` (new) — `lineOpts` (uPlot.Options builder).
- `rust/src/ui/app/widgets/LineChart.tsx` (rewrite) — uPlot component.
- `rust/package.json` (modify) — add `uplot`.
- `rust/e2e/*-snapshots` — regenerated (line cells now uPlot).
- Test files alongside.
- `rust/src/ui/app/widgets/linePath.ts` + `hoverInfo.ts` (+ tests) — now unused; **leave them** (pure, harmless) — deletion is optional and not in this plan.

---

### Task 1: Pure uPlot data helpers

**Files:** Create `rust/src/ui/app/widgets/uplotData.ts` + `uplotData.test.ts`.

**Interfaces:**
- `toUPlotData(xsMs: number[], ys: number[]): [number[], number[]]` — `[xsSeconds, ys]`; `xsMs[i]/1000`, index-aligned to `min(len)`, empty → `[[], []]`.
- `scrollWindow(xsMs: number[], windowMs: number): [number, number]` — `[(last - windowMs)/1000, last/1000]` where `last = xsMs[xsMs.length-1]`; empty → `[0, windowMs/1000]` (or `[0, 1]` when windowMs 0). `windowMs` must be ≥ 1.

- [ ] **Step 1: Failing test**

```ts
import { describe, it, expect } from "vitest";
import { toUPlotData, scrollWindow } from "./uplotData";

describe("toUPlotData", () => {
  it("converts ms→s, index-aligned", () => {
    expect(toUPlotData([0, 1000, 2000], [1, 2, 3])).toEqual([[0, 1, 2], [1, 2, 3]]);
  });
  it("min-length guards + empty", () => {
    expect(toUPlotData([0, 1000], [9])).toEqual([[0], [9]]);
    expect(toUPlotData([], [])).toEqual([[], []]);
  });
});
describe("scrollWindow", () => {
  it("is [last-window, last] in seconds", () => {
    expect(scrollWindow([0, 5000, 10000], 4000)).toEqual([6, 10]); // (10000-4000)/1000 .. 10000/1000
  });
  it("empty → [0, window s]", () => {
    expect(scrollWindow([], 60000)).toEqual([0, 60]);
  });
});
```

- [ ] **Step 2-4:** run (FAIL) → implement → run (PASS) + `tsc`.

```ts
export function toUPlotData(xsMs: number[], ys: number[]): [number[], number[]] {
  const n = Math.min(xsMs.length, ys.length);
  const xs: number[] = [];
  const yy: number[] = [];
  for (let i = 0; i < n; i++) { xs.push(xsMs[i] / 1000); yy.push(ys[i]); }
  return [xs, yy];
}

export function scrollWindow(xsMs: number[], windowMs: number): [number, number] {
  const w = Math.max(1, windowMs);
  if (xsMs.length === 0) return [0, w / 1000];
  const last = xsMs[xsMs.length - 1];
  return [(last - w) / 1000, last / 1000];
}
```

- [ ] **Step 5: Commit** `feat(rust-ui): uPlot data helpers (toUPlotData, scrollWindow)`

---

### Task 2: uPlot options builder

**Files:** Create `rust/src/ui/app/widgets/uplotOpts.ts` + `uplotOpts.test.ts`. Modify `rust/package.json` (add `uplot`).

**Interfaces:** `lineOpts(width: number, height: number): uPlot.Options` — a dark time-series config: `width`/`height`; a single x (time) axis + y axis styled with the INU palette (grid `#1d2632`, axis text `#566273`, font IBM Plex Mono); one series stroked `#38c5e0` (accent) width ~1.4, points off; `scales: { x: { time: true }, y: { auto: true } }`; cursor enabled (native hover), legend off. Background transparent. (uPlot styling is JS config = data; hardcoded hex from the INU palette is acceptable here.)

- [ ] **Step 1: Install + failing test** — `cd rust && npm install uplot`. Then:

```ts
import { describe, it, expect } from "vitest";
import { lineOpts } from "./uplotOpts";

describe("lineOpts", () => {
  it("builds a dark time-series config", () => {
    const o = lineOpts(300, 120);
    expect(o.width).toBe(300);
    expect(o.height).toBe(120);
    expect(o.scales?.x?.time).toBe(true);
    expect(o.scales?.y?.auto).toBe(true);
    // 2 series: x + the value line
    expect(o.series.length).toBe(2);
    expect(o.series[1].stroke).toBe("#38c5e0");
    expect(o.legend?.show).toBe(false);
  });
});
```

- [ ] **Step 2-4:** run (FAIL) → implement `uplotOpts.ts` (import `type uPlot from "uplot"` for the return type; build a valid `uPlot.Options` — `width,height,scales,axes,series,legend,cursor`). Keep it compact + typed (no `any`; uPlot ships types). → run (PASS) + `tsc` + `build`.

> Implementer note: `series[0]` is the x (no stroke); `series[1]` is the value line `{ stroke: "#38c5e0", width: 1.4, points: { show: false } }`. Axes: `{ stroke: "#566273", grid: { stroke: "#1d2632", width: 1 }, ticks: { stroke: "#1d2632" }, font: "10px 'IBM Plex Mono'" }` for both. If a uPlot type is strict, satisfy it precisely — no `any`.

- [ ] **Step 5: Commit** `feat(rust-ui): dark uPlot line options builder + uplot dep`

---

### Task 3: Rewrite LineChart on uPlot

**Files:** Rewrite `rust/src/ui/app/widgets/LineChart.tsx`; extend `LineChart.test.tsx`; add `.linechart-*` CSS as needed to `theme.css`; import `uplot/dist/uPlot.min.css`.

**Interfaces:** unchanged props `{ name, xs, ys, unit, value, scalesOn, zoom?, onZoomBy?, onResetZoom? }`. `data-testid="linechart"`. Renders a chart container + the value overlay (`fmtNum(value)` + unit) + name. Mounts uPlot (guarded), sizes it via JS-fit, updates on data, scrolls the x window.

- [ ] **Step 1: Update the jsdom test** — `LineChart.test.tsx`: render `<LineChart name="Roll" xs={[0,1000,2000]} ys={[1,2,3]} unit="deg" value={3} scalesOn />` and assert: `[data-testid="linechart"]` present; the value overlay shows `fmtNum(3)`; **no crash** (uPlot is NOT constructed in jsdom — the guard prevents it). Drop the old SVG-specific assertions (5 y-labels etc — those belong to the removed SVG impl). Keep an assertion that the name renders.

- [ ] **Step 2: Run → fail** (old SVG render) — `npx vitest run src/ui/app/widgets/LineChart.test.tsx`

- [ ] **Step 3: Implement** — rewrite `LineChart.tsx` mirroring the MapWidget's WebView-safe pattern:

```tsx
import type React from "react";
import { useRef, useEffect } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { fmtNum } from "./gaugeViz";
import { toUPlotData, scrollWindow } from "./uplotData";
import { lineOpts } from "./uplotOpts";

const BASE_WINDOW_MS = 60_000; // 60s default scrolling window

function hasCanvas(): boolean {
  try { return !!document.createElement("canvas").getContext("2d"); } catch { return false; }
}

export function LineChart({ name, xs, ys, unit, value, scalesOn, zoom }: {
  name: string; xs: number[]; ys: number[]; unit: string; value: number;
  scalesOn: boolean; zoom?: number; onZoomBy?: (f: number) => void; onResetZoom?: () => void;
}): React.JSX.Element {
  const elRef = useRef<HTMLDivElement>(null);
  const uRef = useRef<uPlot | null>(null);
  const windowMs = BASE_WINDOW_MS / Math.max(1, zoom ?? 1);

  // Mount uPlot (guarded off in jsdom). JS-size to the nearest real-height ancestor.
  useEffect(() => {
    const el = elRef.current;
    if (!el || !hasCanvas()) return;
    const fit = (): { w: number; h: number } => {
      let h = 0, w = el.clientWidth;
      let node: HTMLElement | null = el;
      while (node && h === 0) { h = node.clientHeight; if (h === 0) node = node.parentElement; }
      if (node) w = node.clientWidth || w;
      return { w: Math.max(50, w), h: Math.max(40, h) };
    };
    const { w, h } = fit();
    const u = new uPlot(lineOpts(w, h), toUPlotData(xs, ys), el);
    u.setScale("x", { min: scrollWindow(xs, windowMs)[0], max: scrollWindow(xs, windowMs)[1] });
    uRef.current = u;
    const ro = new ResizeObserver(() => { const s = fit(); u.setSize({ width: s.w, height: s.h }); });
    ro.observe(el);
    return () => { ro.disconnect(); u.destroy(); uRef.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Stream data + scroll the window. xs.length is the live trigger (arrays mutate in place).
  useEffect(() => {
    const u = uRef.current; if (!u) return;
    u.setData(toUPlotData(xs, ys));
    const [min, max] = scrollWindow(xs, windowMs);
    u.setScale("x", { min, max });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [xs.length, windowMs]);

  return (
    <div data-testid="linechart" className="linechart-container">
      <div className="linechart-head">
        <span className="linechart-name">{name}</span>
        <span className="linechart-value">{fmtNum(value)}<span className="linechart-unit"> {unit}</span></span>
      </div>
      <div ref={elRef} className={`linechart-uplot${scalesOn ? "" : " linechart-noaxes"}`} />
    </div>
  );
}
```

CSS (`theme.css`) — replace the old `.linechart-*` SVG rules with the uPlot container layout (theme vars; the uPlot canvas styles itself via JS config):

```css
.linechart-container{display:flex;flex-direction:column;width:100%;height:100%;gap:2px;position:absolute;inset:0}
.linechart-head{display:flex;align-items:center;justify-content:space-between;font:600 10px/1 var(--mono);color:var(--title);padding:0 2px;flex:none}
.linechart-value{color:var(--text)}
.linechart-unit{font-size:8px;color:var(--dim)}
.linechart-uplot{flex:1;min-height:0;position:relative}
.linechart-noaxes .u-axis{display:none}
.u-legend{display:none}
```

> Implementer notes: (1) the `.linechart-container{position:absolute;inset:0}` ensures the cell body gives it a real height (the same fix the map needed). If the cell body isn't a positioned ancestor, also ensure `.widget-cell-body{position:relative}` exists (it was added in the map phase — verify). (2) `scalesOn=false` hides the axes via the `.linechart-noaxes .u-axis` rule. (3) `zoom` shrinks the window via `windowMs`. (4) Do NOT construct uPlot when `hasCanvas()` is false (jsdom). (5) If uPlot's TS types need a precise cast for `setScale`/`setData`, satisfy them — no `any`.

- [ ] **Step 4: Run → pass** — `npx vitest run src/ui/app/widgets/LineChart.test.tsx`; full `npm test` green; `npx tsc --noEmit` clean; `npm run build` OK (uplot bundles).

- [ ] **Step 5: Commit** `feat(rust-ui): LineChart uses uPlot (scrolling time axis)`

---

### Task 4: Regenerate Playwright baselines

**Files:** `rust/e2e/*-snapshots` (the widgets + interactions baselines include line cells → now uPlot).

- [ ] **Step 1: Clean regen** (avoid the Vite-cache trap):
```bash
cd rust
# stop :1420
rm -rf node_modules/.vite
rm -f e2e/widgets.spec.ts-snapshots/*.png e2e/interactions.spec.ts-snapshots/*.png e2e/shell.spec.ts-snapshots/*.png
npm run e2e:update
npm run e2e   # all green
```
(The map baseline is unaffected; only the dashboards with line cells change. Delete the ones that change so Playwright writes fresh.)

- [ ] **Step 2: Verify** — READ `widgets-chromium-win32.png` + `interactions-chromium-win32.png`; confirm the two line cells now render a **uPlot chart** (axes + a line), gauges/map intact, not blank. If blank/wrong → STOP (likely the jsdom-vs-chromium sizing — uPlot needs the container height; check the `position:absolute;inset:0` fix took).

- [ ] **Step 3: Commit** `test(rust-ui): regen baselines for uPlot line charts`

---

### Task 5: Gate + live verify

- [ ] **Step 1: Full suite** — `cd rust && npx tsc --noEmit && npm test && npm run build && npm run e2e; cd src-tauri && cargo test`. All green.
- [ ] **Step 2: Live verify** — `RIDE_DB=../../data/ride_small.db RIDE_SPEED=1 npm run tauri dev`: line widgets show a **scrolling time x-axis**; the signal trends are visible against a stable y; toggle gauge↔line, resize, hover (uPlot cursor) all work; zoom shrinks the window. Compare to the design intent.
- [ ] **Step 3:** commit any tweaks; finish the branch (PR).

---

## Self-Review

**Spec coverage:** pure data (T1), options (T2), uPlot component with JS-fit sizing + jsdom guard + scrolling window + data streaming (T3), baseline regen (T4), gate+live (T5). Prop contract kept → grid/toggle/resize unchanged. ✓
**Placeholder scan:** No TBD. T1/T2 carry full code; T3 carries the full component (mirrors the proven MapWidget WebView-safe pattern). The uPlot styling hex is data (allowed). ✓
**Type/contract consistency:** `toUPlotData`/`scrollWindow` (T1) consumed by `lineOpts` data + LineChart (T3); `lineOpts` (T2) consumed by T3; LineChart prop contract unchanged so WidgetGrid (Phase 4) is untouched; `xs.length` live-trigger matches the store's in-place mutation (map lesson). ✓
