# UI Phase 4 — Grid Interactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Phase-3 read-only OVERVIEW widget grid into an editable dashboard: drag a parameter onto the grid to add a live gauge, drag widgets to move/reorder, pointer-resize, toggle gauge↔line, remove, and (line charts) zoom the time axis + hover for a value tooltip.

**Architecture:** The widget set becomes **stateful**. A pure model module (`widgetModel.ts`) holds the `LayoutWidget[]` and all edit operations as pure functions (mirroring the design sample's `setState` updaters); a `useWidgets` hook wraps it in `useReducer`, seeded from `defaultWidgets(store.channels())`. Pure pointer/zoom/hover math lives in small testable modules (`dropGrid.ts`, `hoverInfo.ts`, extended `lineViz`). `WidgetGrid` rewires to dispatch edits; param rows become drag sources. Widgets keep referencing `channelId` so values stay **live** (we never freeze a value like the static sample does).

**Tech Stack:** React 19 + TypeScript (strict), Vite, vitest (node + jsdom), Playwright. No new runtime deps.

## Scope note (read before executing)

This plan covers the **grid interactions only**. The **OSM/Leaflet basemap toggle** seen in the sample (the `OSM MAP` button + `osm-ov` Leaflet layer, sample lines 167-170, 429-447) is **deferred to a separate Phase 5**: it is a map *rendering mode* (adds a Leaflet dependency, an imperative map lifecycle, network tiles, and is GUI-only / not unit-testable), orthogonal to the grid editing this phase delivers. Keeping it out makes Phase 4 a cohesive, fully-tested PR. If you want OSM folded into Phase 4 instead, say so before execution.

## Global Constraints

- TypeScript strict, **no `any`**. React 19: `import type React from "react"`; `useId()` for any SVG def ids (no static ids).
- **Theme CSS vars only** — no hardcoded hex in components or CSS. Palette is in `rust/src/ui/app/theme.css` `:root`.
- Data layer (`rust/src/data/store.ts`, `types.ts`, ring buffer) is **reused, not modified**. Widgets read live values via `store.latest(id)` / `store.series(id).arrays()` / `store.gpsTrack()`.
- Pure-math/model modules are deterministic (no `Date`/random), guard empty inputs and divide-by-zero.
- Behavioral parity with the design sample (`docs/sample ui/INU Monitor (standalone-src).html`): copy its exact clamps, pitch, and formulas (cited per task).
- Widgets stay **live** (reference `channelId`); never store a frozen numeric value.
- Grid cell pitch = **168px** (158px cell + 10px gap). Grid: `grid-template-columns:repeat(auto-fill,158px);grid-auto-rows:158px;gap:10px`.
- Clamp constants (from sample `resizeW`/`setSize`/`zoomBy`): line widget `cols ∈ [1,6]`, `rows ∈ [1,4]`; gauge widget is **square** (`cols===rows ∈ [1,6]`); `zoom ∈ [1,8]`.

---

## File Structure

- `rust/src/ui/app/widgets/widgetModel.ts` (new) — `LayoutWidget`/`DragPayload` types + pure edit ops + `seedLayout`. Most logic + tests.
- `rust/src/ui/app/widgets/dropGrid.ts` (new) — pure pointer→cell and resize-step math.
- `rust/src/ui/app/widgets/hoverInfo.ts` (new) — pure line-hover tooltip math.
- `rust/src/ui/app/widgets/linePath.ts` (modify) — add `zoom` param + return `points[]` for hover/zoom.
- `rust/src/ui/app/widgets/useWidgets.ts` (new) — `useReducer` hook over `widgetModel`, seeded from `defaultWidgets`.
- `rust/src/ui/app/widgets/LineMenu.tsx` (new) — line-chart right-click zoom menu.
- `rust/src/ui/app/widgets/WidgetGrid.tsx` (modify) — stateful, editable cells + dropzone.
- `rust/src/ui/app/widgets/LineChart.tsx` (modify) — hover crosshair/tooltip + zoom badge.
- `rust/src/ui/app/widgets/Gauge.tsx` (modify) — accept optional drag/size affordances via the cell wrapper (no internal change; the cell chrome owns drag/resize).
- `rust/src/ui/app/ParamPanel.tsx` + `rust/src/ui/app/paramrow.ts` (modify) — param rows become drag sources.
- `rust/src/ui/app/theme.css` (modify) — cell-header / resize-handle / menu / hover CSS (theme vars).
- `rust/e2e/interactions.spec.ts` (new) — Playwright drag/toggle/resize/hover/remove + baseline.
- Test files alongside each new/modified module.

---

### Task 1: Widget model — types + pure edit operations

**Files:**
- Create: `rust/src/ui/app/widgets/widgetModel.ts`
- Test: `rust/src/ui/app/widgets/widgetModel.test.ts`

**Interfaces:**
- Consumes: `Widget` from `./layout` (`{ id: string; kind: "gauge"|"line"|"map"; channelId?: number; name: string; cols: number; rows: number }`).
- Produces:
  - `interface LayoutWidget { id: string; kind: "gauge"|"line"|"map"; channelId?: number; name: string; unit: string; cols: number; rows: number; col: number; row: number; zoom: number }`
  - `interface DragPayload { channelId?: number; name: string; unit: string }`
  - `seedLayout(widgets: Widget[], units: Map<number,string>): LayoutWidget[]`
  - `addWidget(ws: LayoutWidget[], d: DragPayload, col: number, row: number, id: string): LayoutWidget[]`
  - `moveWidget(ws, id: string, col: number, row: number): LayoutWidget[]`
  - `reorderWidgets(ws, draggedId: string, targetId: string | null): LayoutWidget[]`
  - `setSize(ws, id: string, cols: number, rows: number): LayoutWidget[]`
  - `resizeW(ws, id: string, dc: number, dr: number): LayoutWidget[]`
  - `toggleType(ws, id: string): LayoutWidget[]`
  - `removeWidget(ws, id: string): LayoutWidget[]`
  - `zoomBy(ws, id: string, f: number): LayoutWidget[]`
  - `resetZoom(ws, id: string): LayoutWidget[]`

- [ ] **Step 1: Write the failing test**

```ts
// widgetModel.test.ts
import { describe, it, expect } from "vitest";
import {
  seedLayout, addWidget, moveWidget, reorderWidgets, setSize, resizeW,
  toggleType, removeWidget, zoomBy, resetZoom, type LayoutWidget,
} from "./widgetModel";
import type { Widget } from "./layout";

const g = (id: string, over: Partial<LayoutWidget> = {}): LayoutWidget => ({
  id, kind: "gauge", channelId: 1, name: "G", unit: "g", cols: 1, rows: 1, col: 1, row: 1, zoom: 1, ...over,
});

describe("seedLayout", () => {
  it("places a 4x4 map then a 1x1 gauge without overlap, all explicit col/row", () => {
    const ws: Widget[] = [
      { id: "map", kind: "map", name: "Flight Track", cols: 4, rows: 4 },
      { id: "gauge-1", kind: "gauge", channelId: 1, name: "Roll", cols: 1, rows: 1 },
    ];
    const out = seedLayout(ws, new Map([[1, "deg"]]));
    expect(out[0]).toMatchObject({ id: "map", col: 1, row: 1, cols: 4, rows: 4, zoom: 1 });
    // gauge cannot fit in cols 1..4 of rows 1..4 (occupied by map) → placed at col 5
    expect(out[1]).toMatchObject({ id: "gauge-1", col: 5, row: 1, unit: "deg" });
    // no two widgets share a cell
    const cells = new Set<string>();
    for (const w of out) for (let c = w.col; c < w.col + w.cols; c++) for (let r = w.row; r < w.row + w.rows; r++) {
      const k = `${c},${r}`; expect(cells.has(k)).toBe(false); cells.add(k);
    }
  });
});

describe("addWidget", () => {
  it("adds a 1x1 live gauge at the dropped cell referencing channelId", () => {
    const out = addWidget([], { channelId: 7, name: "Pitch", unit: "deg" }, 3, 2, "w-1");
    expect(out).toHaveLength(1);
    expect(out[0]).toMatchObject({ id: "w-1", kind: "gauge", channelId: 7, name: "Pitch", unit: "deg", cols: 1, rows: 1, col: 3, row: 2, zoom: 1 });
  });
});

describe("moveWidget", () => {
  it("updates col/row of the matching id only", () => {
    const out = moveWidget([g("a", { col: 1, row: 1 }), g("b", { col: 2, row: 1 })], "b", 4, 3);
    expect(out.find((w) => w.id === "b")).toMatchObject({ col: 4, row: 3 });
    expect(out.find((w) => w.id === "a")).toMatchObject({ col: 1, row: 1 });
  });
});

describe("reorderWidgets", () => {
  it("moves dragged before target; null target → end", () => {
    const ws = [g("a"), g("b"), g("c")];
    expect(reorderWidgets(ws, "c", "a").map((w) => w.id)).toEqual(["c", "a", "b"]);
    expect(reorderWidgets(ws, "a", null).map((w) => w.id)).toEqual(["b", "c", "a"]);
  });
});

describe("setSize", () => {
  it("clamps line to cols[1,6]/rows[1,4]", () => {
    const out = setSize([g("l", { kind: "line", cols: 2, rows: 1 })], "l", 99, 99);
    expect(out[0]).toMatchObject({ cols: 6, rows: 4 });
  });
  it("forces gauge square = max(cols,rows) clamped [1,6]", () => {
    const out = setSize([g("a", { cols: 1, rows: 1 })], "a", 3, 5);
    expect(out[0]).toMatchObject({ cols: 5, rows: 5 });
  });
});

describe("resizeW", () => {
  it("is a no-op on gauges and relative+clamped on lines", () => {
    expect(resizeW([g("a")], "a", 2, 2)[0]).toMatchObject({ cols: 1, rows: 1 });
    const out = resizeW([g("l", { kind: "line", cols: 2, rows: 1 })], "l", 1, 1);
    expect(out[0]).toMatchObject({ cols: 3, rows: 2 });
  });
});

describe("toggleType", () => {
  it("gauge→line bumps cols to ≥2; line→gauge resets to 1x1", () => {
    const toLine = toggleType([g("a", { cols: 1, rows: 1 })], "a")[0];
    expect(toLine).toMatchObject({ kind: "line", cols: 2, rows: 1 });
    const toGauge = toggleType([g("l", { kind: "line", cols: 3, rows: 2 })], "l")[0];
    expect(toGauge).toMatchObject({ kind: "gauge", cols: 1, rows: 1 });
  });
});

describe("removeWidget", () => {
  it("drops the matching id", () => {
    expect(removeWidget([g("a"), g("b")], "a").map((w) => w.id)).toEqual(["b"]);
  });
});

describe("zoom", () => {
  it("zoomBy multiplies clamped to [1,8]; resetZoom → 1", () => {
    const z = zoomBy([g("l", { kind: "line", zoom: 4 })], "l", 4)[0];
    expect(z.zoom).toBe(8);
    const lo = zoomBy([g("l", { kind: "line", zoom: 1 })], "l", 0.5)[0];
    expect(lo.zoom).toBe(1);
    expect(resetZoom([g("l", { kind: "line", zoom: 5 })], "l")[0].zoom).toBe(1);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/widgetModel.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement `widgetModel.ts`**

```ts
import type { Widget } from "./layout";

export interface LayoutWidget {
  id: string;
  kind: "gauge" | "line" | "map";
  channelId?: number;
  name: string;
  unit: string;
  cols: number;
  rows: number;
  col: number;
  row: number;
  zoom: number;
}

export interface DragPayload {
  channelId?: number;
  name: string;
  unit: string;
}

const LINE_MAX_COLS = 6;
const LINE_MAX_ROWS = 4;
const GAUGE_MAX = 6;
const ZOOM_MIN = 1;
const ZOOM_MAX = 8;
const SEED_COLS = 8; // virtual width used only for initial packing

const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));

/** First-fit row-major packer: place each widget at the first free cell in a SEED_COLS-wide grid. */
export function seedLayout(widgets: Widget[], units: Map<number, string>): LayoutWidget[] {
  const occupied = new Set<string>();
  const fits = (col: number, row: number, cols: number, rows: number): boolean => {
    if (col + cols - 1 > SEED_COLS) return false;
    for (let c = col; c < col + cols; c++) for (let r = row; r < row + rows; r++) if (occupied.has(`${c},${r}`)) return false;
    return true;
  };
  const mark = (col: number, row: number, cols: number, rows: number): void => {
    for (let c = col; c < col + cols; c++) for (let r = row; r < row + rows; r++) occupied.add(`${c},${r}`);
  };
  return widgets.map((w) => {
    let placed = { col: 1, row: 1 };
    outer: for (let row = 1; row < 1000; row++) for (let col = 1; col <= SEED_COLS; col++) {
      if (fits(col, row, w.cols, w.rows)) { placed = { col, row }; break outer; }
    }
    mark(placed.col, placed.row, w.cols, w.rows);
    return {
      id: w.id, kind: w.kind, channelId: w.channelId, name: w.name,
      unit: w.channelId !== undefined ? (units.get(w.channelId) ?? "") : "",
      cols: w.cols, rows: w.rows, col: placed.col, row: placed.row, zoom: 1,
    };
  });
}

export function addWidget(ws: LayoutWidget[], d: DragPayload, col: number, row: number, id: string): LayoutWidget[] {
  return ws.concat([{
    id, kind: "gauge", channelId: d.channelId, name: d.name, unit: d.unit || "",
    cols: 1, rows: 1, col: Math.max(1, col), row: Math.max(1, row), zoom: 1,
  }]);
}

export function moveWidget(ws: LayoutWidget[], id: string, col: number, row: number): LayoutWidget[] {
  return ws.map((w) => (w.id !== id ? w : { ...w, col: Math.max(1, col), row: Math.max(1, row) }));
}

export function reorderWidgets(ws: LayoutWidget[], draggedId: string, targetId: string | null): LayoutWidget[] {
  const arr = ws.slice();
  const from = arr.findIndex((w) => w.id === draggedId);
  if (from < 0) return ws;
  const moved = arr.splice(from, 1)[0];
  let ti = targetId == null ? arr.length : arr.findIndex((w) => w.id === targetId);
  if (ti < 0) ti = arr.length;
  arr.splice(ti, 0, moved);
  return arr;
}

export function setSize(ws: LayoutWidget[], id: string, cols: number, rows: number): LayoutWidget[] {
  return ws.map((w) => {
    if (w.id !== id) return w;
    if (w.kind === "gauge") {
      const s = clamp(Math.max(cols, rows), 1, GAUGE_MAX);
      return { ...w, cols: s, rows: s };
    }
    return { ...w, cols: clamp(cols, 1, LINE_MAX_COLS), rows: clamp(rows, 1, LINE_MAX_ROWS) };
  });
}

export function resizeW(ws: LayoutWidget[], id: string, dc: number, dr: number): LayoutWidget[] {
  return ws.map((w) => {
    if (w.id !== id || w.kind === "gauge") return w;
    return { ...w, cols: clamp(w.cols + dc, 1, LINE_MAX_COLS), rows: clamp(w.rows + dr, 1, LINE_MAX_ROWS) };
  });
}

export function toggleType(ws: LayoutWidget[], id: string): LayoutWidget[] {
  return ws.map((w) => {
    if (w.id !== id) return w;
    return w.kind === "gauge"
      ? { ...w, kind: "line", cols: Math.max(2, w.cols), rows: Math.max(1, w.rows) }
      : { ...w, kind: "gauge", cols: 1, rows: 1 };
  });
}

export function removeWidget(ws: LayoutWidget[], id: string): LayoutWidget[] {
  return ws.filter((w) => w.id !== id);
}

export function zoomBy(ws: LayoutWidget[], id: string, f: number): LayoutWidget[] {
  return ws.map((w) => (w.id !== id ? w : { ...w, zoom: clamp(w.zoom * f, ZOOM_MIN, ZOOM_MAX) }));
}

export function resetZoom(ws: LayoutWidget[], id: string): LayoutWidget[] {
  return ws.map((w) => (w.id !== id ? w : { ...w, zoom: 1 }));
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/widgetModel.test.ts`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/widgetModel.ts rust/src/ui/app/widgets/widgetModel.test.ts
git commit -m "feat(rust-ui): editable widget model (LayoutWidget + pure ops)"
```

---

### Task 2: Pointer→cell + resize-step math

**Files:**
- Create: `rust/src/ui/app/widgets/dropGrid.ts`
- Test: `rust/src/ui/app/widgets/dropGrid.test.ts`

**Interfaces:**
- Produces:
  - `cellFromPoint(rect: { left: number; top: number }, clientX: number, clientY: number, scrollLeft: number, scrollTop: number, pitch?: number): { col: number; row: number }`
  - `resizeStep(delta: number, pitch?: number): number`

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from "vitest";
import { cellFromPoint, resizeStep } from "./dropGrid";

describe("cellFromPoint (pitch 168)", () => {
  it("maps a point in the first cell to (1,1)", () => {
    expect(cellFromPoint({ left: 0, top: 0 }, 10, 10, 0, 0)).toEqual({ col: 1, row: 1 });
  });
  it("maps the 3rd column / 2nd row by pitch", () => {
    expect(cellFromPoint({ left: 0, top: 0 }, 2 * 168 + 5, 1 * 168 + 5, 0, 0)).toEqual({ col: 3, row: 2 });
  });
  it("adds scroll offset and never returns < 1", () => {
    expect(cellFromPoint({ left: 100, top: 100 }, 100, 100, 168, 0)).toEqual({ col: 2, row: 1 });
    expect(cellFromPoint({ left: 500, top: 0 }, 0, 0, 0, 0)).toEqual({ col: 1, row: 1 });
  });
});

describe("resizeStep (pitch 168)", () => {
  it("0 within deadzone → 0 steps; >144 → +1; < -144 → -1", () => {
    expect(resizeStep(0)).toBe(0);
    expect(resizeStep(100)).toBe(0);
    expect(resizeStep(150)).toBe(1);
    expect(resizeStep(-150)).toBe(-1);
    expect(resizeStep(2 * 168)).toBe(2);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/dropGrid.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement `dropGrid.ts`** (formulas copied from sample lines 482-485 and 564)

```ts
const PITCH = 168;

export function cellFromPoint(
  rect: { left: number; top: number },
  clientX: number,
  clientY: number,
  scrollLeft: number,
  scrollTop: number,
  pitch: number = PITCH,
): { col: number; row: number } {
  const col = Math.max(1, Math.floor((clientX - rect.left + scrollLeft) / pitch) + 1);
  const row = Math.max(1, Math.floor((clientY - rect.top + scrollTop) / pitch) + 1);
  return { col, row };
}

export function resizeStep(delta: number, pitch: number = PITCH): number {
  const dead = pitch - 24; // 144 at pitch 168 (sample's deadzone)
  return delta >= 0 ? Math.floor((delta + dead) / pitch) : Math.ceil((delta - dead) / pitch);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/dropGrid.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/dropGrid.ts rust/src/ui/app/widgets/dropGrid.test.ts
git commit -m "feat(rust-ui): grid pointer→cell + resize-step math"
```

---

### Task 3: Extend `lineViz` with zoom window + display points

**Files:**
- Modify: `rust/src/ui/app/widgets/linePath.ts`
- Test: `rust/src/ui/app/widgets/linePath.test.ts` (extend)

**Interfaces:**
- Consumes: existing `fmtNum` from `./gaugeViz`.
- Produces (new signature — `zoom` inserted before `viewW`, plus `points`):
  - `lineViz(xs: number[], ys: number[], value: number, zoom?: number, viewW?: number, viewH?: number): { path: string; yHi: string; yQ3: string; yMid: string; yQ1: string; yLo: string; xs0: string; xMid: string; xLast: string; points: Array<{ px: number; yv: number; val: number; ts: number }> }`
- Behavior added to the Phase-3 value-centered implementation:
  - `zoom` (default 1) shrinks the visible window to the **last** `win = clamp(round(N / zoom), 1, N)` samples of `ys`/`xs` (`N = ys.length`).
  - `points[i]` for each visible sample: `px = win > 1 ? viewW * i / (win - 1) : 0`; `yv` = the same clamped centered-y used to build the path; `val = ys[start + i]`; `ts = xs[start + i] - xs[N - 1]` (≤ 0, seconds).
  - `path` is built from the **windowed** points (so zoom changes the curve).
  - x-axis labels derive from the **windowed** span (`xs[N-1] - xs[start]`).

- [ ] **Step 1: Write the failing test (extend existing file)**

```ts
// add to linePath.test.ts
import { lineViz } from "./linePath";

describe("lineViz zoom + points", () => {
  const ys = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
  const xs = ys.map((_, i) => i); // seconds

  it("returns one display point per sample at zoom 1", () => {
    const r = lineViz(xs, ys, 5, 1);
    expect(r.points).toHaveLength(10);
    expect(r.points[0]).toMatchObject({ px: 0 });
    expect(r.points[9].px).toBeCloseTo(200, 5);
    // ts is relative seconds, last = 0
    expect(r.points[9].ts).toBe(0);
    expect(r.points[0].ts).toBe(-9);
  });

  it("zoom 2 keeps only the last half (5 samples)", () => {
    const r = lineViz(xs, ys, 5, 2);
    expect(r.points).toHaveLength(5);
    expect(r.points.map((p) => p.val)).toEqual([5, 6, 7, 8, 9]);
    expect(r.path.startsWith("M")).toBe(true);
  });

  it("empty series → empty points + path", () => {
    const r = lineViz([], [], 0, 1);
    expect(r.points).toEqual([]);
    expect(r.path).toBe("");
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/linePath.test.ts`
Expected: FAIL (extra `zoom`/`points` not present; existing tests that call `lineViz(xs,ys,value)` still pass since `zoom` defaults).

- [ ] **Step 3: Implement** — update `linePath.ts`. Keep the Phase-3 value-centered band; add windowing + points. Reference the existing function and replace its body:

```ts
import { fmtNum } from "./gaugeViz";

const DASH = "—";

export function lineViz(
  xs: number[],
  ys: number[],
  value: number,
  zoom: number = 1,
  viewW: number = 200,
  viewH: number = 80,
): {
  path: string; yHi: string; yQ3: string; yMid: string; yQ1: string; yLo: string;
  xs0: string; xMid: string; xLast: string;
  points: Array<{ px: number; yv: number; val: number; ts: number }>;
} {
  const empty = {
    path: "", yHi: DASH, yQ3: DASH, yMid: DASH, yQ1: DASH, yLo: DASH,
    xs0: DASH, xMid: DASH, xLast: DASH, points: [] as Array<{ px: number; yv: number; val: number; ts: number }>,
  };
  const N = ys.length;
  if (N === 0 || xs.length === 0) return empty;

  const v = value;
  const lr = Math.max(0.5, Math.abs(v) * 0.5);
  const top = 6, bot = viewH - 6, mid = viewH / 2, half = mid - top;

  const z = Math.max(1, zoom);
  const win = Math.min(N, Math.max(1, Math.round(N / z)));
  const start = N - win;
  const lastX = xs[N - 1];

  const points: Array<{ px: number; yv: number; val: number; ts: number }> = [];
  let path = "";
  for (let i = 0; i < win; i++) {
    const val = ys[start + i];
    const yv = Math.min(bot, Math.max(top, mid - ((val - v) / lr) * half));
    const px = win > 1 ? (viewW * i) / (win - 1) : 0;
    path += (i ? "L" : "M") + px.toFixed(1) + " " + yv.toFixed(1);
    points.push({ px, yv, val, ts: xs[start + i] - lastX });
  }

  const span = lastX - xs[start];
  const fts = (s: number): string => (s < 10 ? s.toFixed(1) : s.toFixed(0));
  const xs0 = "-" + fts(span) + "s";
  const xMid = "-" + fts(span / 2) + "s";
  const xLast = "0s";

  return {
    path,
    yHi: fmtNum(v + lr), yQ3: fmtNum(v + lr / 2), yMid: fmtNum(v), yQ1: fmtNum(v - lr / 2), yLo: fmtNum(v - lr),
    xs0, xMid, xLast, points,
  };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/linePath.test.ts && npx tsc --noEmit`
Expected: PASS (new + existing); tsc clean. (LineChart still calls `lineViz(xs,ys,value)` — `zoom` defaults; `points` is additive.)

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/linePath.ts rust/src/ui/app/widgets/linePath.test.ts
git commit -m "feat(rust-ui): lineViz zoom window + display points"
```

---

### Task 4: Hover tooltip math

**Files:**
- Create: `rust/src/ui/app/widgets/hoverInfo.ts`
- Test: `rust/src/ui/app/widgets/hoverInfo.test.ts`

**Interfaces:**
- Consumes: `fmtNum` from `./gaugeViz`; the `points` array shape from `lineViz`.
- Produces:
  - `hoverInfo(points: Array<{ px: number; yv: number; val: number; ts: number }>, relX: number, unit: string, viewW?: number, viewH?: number): { active: boolean; hxPct: string; hyPct: string; hVal: string; hT: string; tipLeftPct: string }`
  - When `points` is empty → `{ active: false, hxPct:"0%", hyPct:"0%", hVal:"", hT:"", tipLeftPct:"50%" }`.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from "vitest";
import { hoverInfo } from "./hoverInfo";

const pts = [
  { px: 0, yv: 40, val: 5, ts: -9 },
  { px: 100, yv: 20, val: 7, ts: -4.5 },
  { px: 200, yv: 6, val: 9, ts: 0 },
];

describe("hoverInfo", () => {
  it("snaps to nearest sample and reports pcts/value/time", () => {
    const r = hoverInfo(pts, 1, "deg"); // relX 1 → last point
    expect(r.active).toBe(true);
    expect(r.hxPct).toBe("100.00%");
    expect(r.hyPct).toBe("7.50%"); // 6/80
    expect(r.hVal).toBe("9.000 deg");
    expect(r.hT).toBe("-0.0s");
  });
  it("clamps tip left to [20,80]%", () => {
    expect(hoverInfo(pts, 0, "deg").tipLeftPct).toBe("20.00%");
    expect(hoverInfo(pts, 1, "deg").tipLeftPct).toBe("80.00%");
  });
  it("inactive on empty points", () => {
    expect(hoverInfo([], 0.5, "deg").active).toBe(false);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/hoverInfo.test.ts`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement `hoverInfo.ts`** (formulas from sample lines 602-621)

```ts
import { fmtNum } from "./gaugeViz";

export function hoverInfo(
  points: Array<{ px: number; yv: number; val: number; ts: number }>,
  relX: number,
  unit: string,
  viewW: number = 200,
  viewH: number = 80,
): { active: boolean; hxPct: string; hyPct: string; hVal: string; hT: string; tipLeftPct: string } {
  if (points.length === 0) {
    return { active: false, hxPct: "0%", hyPct: "0%", hVal: "", hT: "", tipLeftPct: "50%" };
  }
  const idx = Math.round(Math.min(1, Math.max(0, relX)) * (points.length - 1));
  const p = points[Math.min(points.length - 1, Math.max(0, idx))];
  const at = Math.abs(p.ts);
  return {
    active: true,
    hxPct: ((p.px / viewW) * 100).toFixed(2) + "%",
    hyPct: ((p.yv / viewH) * 100).toFixed(2) + "%",
    hVal: fmtNum(p.val) + (unit ? " " + unit : ""),
    hT: "-" + (at < 10 ? at.toFixed(1) : at.toFixed(0)) + "s",
    tipLeftPct: Math.max(20, Math.min(80, (p.px / viewW) * 100)).toFixed(2) + "%",
  };
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/hoverInfo.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/hoverInfo.ts rust/src/ui/app/widgets/hoverInfo.test.ts
git commit -m "feat(rust-ui): line-chart hover tooltip math"
```

---

### Task 5: `useWidgets` hook (stateful model, seeded from defaults)

**Files:**
- Create: `rust/src/ui/app/widgets/useWidgets.ts`
- Test: `rust/src/ui/app/widgets/useWidgets.test.tsx`

**Interfaces:**
- Consumes: `widgetModel` ops + `LayoutWidget`/`DragPayload`; `defaultWidgets` from `./layout`; `ChannelMeta` from `../../../types`.
- Produces:
  - `useWidgets(channels: ChannelMeta[]): { widgets: LayoutWidget[]; add(d: DragPayload, col: number, row: number): void; move(id: string, col: number, row: number): void; reorder(draggedId: string, targetId: string | null): void; setSize(id: string, cols: number, rows: number): void; resize(id: string, dc: number, dr: number): void; toggle(id: string): void; remove(id: string): void; zoomBy(id: string, f: number): void; resetZoom(id: string): void }`
  - Seeds once from `seedLayout(defaultWidgets(channels), unitMap)`; a numeric counter mints ids `w-1, w-2, …` for `add`. Re-seeding when `channels` identity changes is **not** required (channels are stable after meta) — seed lazily on first non-empty `channels`.

- [ ] **Step 1: Write the failing test**

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup, act } from "@testing-library/react";
import type React from "react";
import { useWidgets } from "./useWidgets";
import type { ChannelMeta } from "../../../types";

afterEach(cleanup);

const channels: ChannelMeta[] = [
  { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I01" },
  { id: 2, name: "SkyPitch", column_name: "sky_pitch", unit: "g", type: "real", min: -2, max: 2, widget: "gauge", display_order: 2, addr: "I02" },
];

function Harness(): React.JSX.Element {
  const w = useWidgets(channels);
  return (
    <div>
      <div data-testid="count">{w.widgets.length}</div>
      <button data-testid="add" onClick={() => w.add({ channelId: 1, name: "Roll", unit: "deg" }, 2, 2)}>add</button>
      <button data-testid="rm" onClick={() => w.remove(w.widgets[w.widgets.length - 1].id)}>rm</button>
    </div>
  );
}

describe("useWidgets", () => {
  it("seeds from defaultWidgets and supports add/remove with minted ids", () => {
    render(<Harness />);
    const start = Number(screen.getByTestId("count").textContent);
    expect(start).toBeGreaterThan(0); // gauge for ch2 at least
    act(() => { screen.getByTestId("add").click(); });
    expect(Number(screen.getByTestId("count").textContent)).toBe(start + 1);
    act(() => { screen.getByTestId("rm").click(); });
    expect(Number(screen.getByTestId("count").textContent)).toBe(start);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/useWidgets.test.tsx`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement `useWidgets.ts`**

```ts
import { useReducer, useMemo } from "react";
import type { ChannelMeta } from "../../../types";
import { defaultWidgets } from "./layout";
import {
  seedLayout, addWidget, moveWidget, reorderWidgets, setSize as modelSetSize,
  resizeW, toggleType, removeWidget, zoomBy as modelZoomBy, resetZoom as modelResetZoom,
  type LayoutWidget, type DragPayload,
} from "./widgetModel";

interface State { widgets: LayoutWidget[]; nextId: number }

type Action =
  | { t: "add"; d: DragPayload; col: number; row: number }
  | { t: "move"; id: string; col: number; row: number }
  | { t: "reorder"; draggedId: string; targetId: string | null }
  | { t: "setSize"; id: string; cols: number; rows: number }
  | { t: "resize"; id: string; dc: number; dr: number }
  | { t: "toggle"; id: string }
  | { t: "remove"; id: string }
  | { t: "zoomBy"; id: string; f: number }
  | { t: "resetZoom"; id: string };

function reducer(s: State, a: Action): State {
  switch (a.t) {
    case "add": return { widgets: addWidget(s.widgets, a.d, a.col, a.row, `w-${s.nextId}`), nextId: s.nextId + 1 };
    case "move": return { ...s, widgets: moveWidget(s.widgets, a.id, a.col, a.row) };
    case "reorder": return { ...s, widgets: reorderWidgets(s.widgets, a.draggedId, a.targetId) };
    case "setSize": return { ...s, widgets: modelSetSize(s.widgets, a.id, a.cols, a.rows) };
    case "resize": return { ...s, widgets: resizeW(s.widgets, a.id, a.dc, a.dr) };
    case "toggle": return { ...s, widgets: toggleType(s.widgets, a.id) };
    case "remove": return { ...s, widgets: removeWidget(s.widgets, a.id) };
    case "zoomBy": return { ...s, widgets: modelZoomBy(s.widgets, a.id, a.f) };
    case "resetZoom": return { ...s, widgets: modelResetZoom(s.widgets, a.id) };
    default: return s;
  }
}

export function useWidgets(channels: ChannelMeta[]): {
  widgets: LayoutWidget[];
  add(d: DragPayload, col: number, row: number): void;
  move(id: string, col: number, row: number): void;
  reorder(draggedId: string, targetId: string | null): void;
  setSize(id: string, cols: number, rows: number): void;
  resize(id: string, dc: number, dr: number): void;
  toggle(id: string): void;
  remove(id: string): void;
  zoomBy(id: string, f: number): void;
  resetZoom(id: string): void;
} {
  const init = useMemo<State>(() => {
    const units = new Map<number, string>();
    for (const ch of channels) units.set(ch.id, ch.unit);
    return { widgets: seedLayout(defaultWidgets(channels), units), nextId: 1 };
    // channels are stable after meta; seeding once is intended.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const [state, dispatch] = useReducer(reducer, init);
  return {
    widgets: state.widgets,
    add: (d, col, row) => dispatch({ t: "add", d, col, row }),
    move: (id, col, row) => dispatch({ t: "move", id, col, row }),
    reorder: (draggedId, targetId) => dispatch({ t: "reorder", draggedId, targetId }),
    setSize: (id, cols, rows) => dispatch({ t: "setSize", id, cols, rows }),
    resize: (id, dc, dr) => dispatch({ t: "resize", id, dc, dr }),
    toggle: (id) => dispatch({ t: "toggle", id }),
    remove: (id) => dispatch({ t: "remove", id }),
    zoomBy: (id, f) => dispatch({ t: "zoomBy", id, f }),
    resetZoom: (id) => dispatch({ t: "resetZoom", id }),
  };
}
```

> Note: if the seed must react to channels arriving after first mount, the implementer may key the hosting component on `channels.length>0`. The `WidgetGrid` in Task 7 is only mounted once channels exist (meta has arrived), so seed-once is correct here.

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/useWidgets.test.tsx && npx tsc --noEmit`
Expected: PASS; tsc clean.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/useWidgets.ts rust/src/ui/app/widgets/useWidgets.test.tsx
git commit -m "feat(rust-ui): useWidgets reducer hook seeded from defaults"
```

---

### Task 6: Param rows become drag sources

**Files:**
- Modify: `rust/src/ui/app/paramrow.ts` (row view-model) and `rust/src/ui/app/ParamPanel.tsx` (render)
- Test: `rust/src/ui/app/ParamPanel.test.tsx` (extend)

**Interfaces:**
- Consumes: `DragPayload` from `./widgets/widgetModel`.
- Produces: each rendered param row carries `draggable={true}`, `data-prow={channelId}`, and an `onDragStart` that sets `dataTransfer` to a JSON `DragPayload` (`{ channelId, name, unit }`) and `effectAllowed="copy"`. The drag payload type is the contract WidgetGrid's drop handler reads.

- [ ] **Step 1: Write the failing test** (extend `ParamPanel.test.tsx`)

```tsx
// @vitest-environment jsdom
// (add to existing ParamPanel.test.tsx)
it("param rows are draggable and emit a DragPayload on dragstart", () => {
  // render ParamPanel with a store containing channel id 1 "Roll" unit "deg" (reuse the file's existing store setup)
  const row = document.querySelector('[data-prow="1"]') as HTMLElement;
  expect(row).toBeTruthy();
  expect(row.getAttribute("draggable")).toBe("true");
  let captured = "";
  const dt = { effectAllowed: "", setData: (_t: string, v: string) => { captured = v; } };
  row.dispatchEvent(Object.assign(new Event("dragstart", { bubbles: true }), { dataTransfer: dt }));
  expect(JSON.parse(captured)).toMatchObject({ channelId: 1, name: expect.any(String), unit: "deg" });
});
```

> The implementer reuses whatever store/render scaffolding the existing `ParamPanel.test.tsx` already uses; only the drag assertions are new. If React's synthetic `onDragStart` can't read a hand-built event in jsdom, attach the handler so it reads `e.dataTransfer` and have the test pass a `dataTransfer` mock (as above), matching how the sample reads `e.dataTransfer.setData`.

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/ParamPanel.test.tsx`
Expected: FAIL (rows not draggable yet).

- [ ] **Step 3: Implement** — in `paramrow.ts` ensure the row view-model exposes `channelId`, `name`, `unit`. In `ParamPanel.tsx`, on each row element add:

```tsx
<div
  className="param-row"
  data-prow={row.channelId}
  draggable
  onDragStart={(e) => {
    const payload = { channelId: row.channelId, name: row.name, unit: row.unit };
    try {
      e.dataTransfer.effectAllowed = "copy";
      e.dataTransfer.setData("application/x-inu-param", JSON.stringify(payload));
      e.dataTransfer.setData("text/plain", JSON.stringify(payload));
    } catch {
      /* jsdom may lack dataTransfer; ignored */
    }
  }}
>
  …existing row content…
</div>
```

(Use the existing row class/markup; only add `data-prow`, `draggable`, `onDragStart`. Keep enum/critical styling unchanged.)

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/ParamPanel.test.tsx && npx tsc --noEmit`
Expected: PASS; tsc clean.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/paramrow.ts rust/src/ui/app/ParamPanel.tsx rust/src/ui/app/ParamPanel.test.tsx
git commit -m "feat(rust-ui): param rows are drag sources (DragPayload)"
```

---

### Task 7: WidgetGrid — stateful editable grid + dropzone + cell chrome

**Files:**
- Modify: `rust/src/ui/app/widgets/WidgetGrid.tsx`
- Modify: `rust/src/ui/app/theme.css` (cell header / resize handle classes — theme vars)
- Test: `rust/src/ui/app/widgets/WidgetGrid.test.tsx` (extend)

**Interfaces:**
- Consumes: `useWidgets` (Task 5), `cellFromPoint` (Task 2), `DragPayload`/`LayoutWidget` (Task 1), `Gauge`/`LineChart`/`MapWidget` (existing). Props unchanged: `{ store: TelemetryStore; scalesOn: boolean }`.
- Produces: the dropzone container `data-dropzone`; each cell `data-widget={id}`, `draggable`, explicit `gridColumn:"{col} / span {cols}"`, `gridRow:"{row} / span {rows}"`; header with a ☰ drag affordance + name, a **toggle** control (`LINE`/`GAUGE` label) for non-map widgets, a **×** remove control, and a bottom-right **resize handle** (`onPointerDown`). Map widgets keep their own header (no toggle); they are draggable/removable but **not** resized below 2×2 (kept simple: map is fixed 4×4, not resizable — omit its resize handle).

- [ ] **Step 1: Write the failing test** (extend `WidgetGrid.test.tsx`)

```tsx
// @vitest-environment jsdom
// (extend existing WidgetGrid.test.tsx, which already builds a populated store via the mock fixture)
import { fireEvent } from "@testing-library/react";

it("dropping a param payload on the grid adds a gauge cell", () => {
  // render <WidgetGrid store={store} scalesOn /> (reuse existing setup)
  const before = document.querySelectorAll('[data-testid="gauge"]').length;
  const dz = document.querySelector('[data-dropzone]') as HTMLElement;
  const payload = JSON.stringify({ channelId: 1, name: "Roll", unit: "deg" });
  const dt = { getData: () => payload, dropEffect: "", types: ["text/plain"] };
  fireEvent.dragOver(dz, { dataTransfer: dt });
  fireEvent.drop(dz, { dataTransfer: dt, clientX: 5, clientY: 5 });
  expect(document.querySelectorAll('[data-testid="gauge"]').length).toBe(before + 1);
});

it("× removes a widget and toggle switches a gauge to a line", () => {
  const cell = document.querySelector('[data-widget]') as HTMLElement;
  const id = cell.getAttribute("data-widget");
  // toggle the first gauge → becomes a line
  const toggle = cell.querySelector('[data-act="toggle"]') as HTMLElement;
  fireEvent.click(toggle);
  expect(document.querySelector(`[data-widget="${id}"] [data-testid="linechart"]`)).toBeTruthy();
  // remove it
  const rm = document.querySelector(`[data-widget="${id}"] [data-act="remove"]`) as HTMLElement;
  fireEvent.click(rm);
  expect(document.querySelector(`[data-widget="${id}"]`)).toBeNull();
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/WidgetGrid.test.tsx`
Expected: FAIL (no dropzone/data-widget/toggle yet).

- [ ] **Step 3: Implement** — rewrite `WidgetGrid.tsx`:

```tsx
import type React from "react";
import { useRef } from "react";
import type { TelemetryStore } from "../../../data/store";
import { useWidgets } from "./useWidgets";
import { cellFromPoint } from "./dropGrid";
import { resizeStep } from "./dropGrid";
import type { DragPayload, LayoutWidget } from "./widgetModel";
import { Gauge } from "./Gauge";
import { LineChart } from "./LineChart";
import { MapWidget } from "./MapWidget";

interface WidgetGridProps { store: TelemetryStore; scalesOn: boolean }

function parsePayload(dt: DataTransfer): DragPayload | null {
  for (const t of ["application/x-inu-param", "text/plain"]) {
    try {
      const raw = dt.getData(t);
      if (raw) { const p = JSON.parse(raw) as DragPayload; if (typeof p.name === "string") return p; }
    } catch { /* ignore */ }
  }
  return null;
}

export function WidgetGrid({ store, scalesOn }: WidgetGridProps): React.JSX.Element {
  const wm = useWidgets(store.channels());
  const dragId = useRef<string | null>(null);

  const onDropGrid = (e: React.DragEvent<HTMLDivElement>): void => {
    e.preventDefault();
    const dz = e.currentTarget;
    const rect = dz.getBoundingClientRect();
    const { col, row } = cellFromPoint(rect, e.clientX, e.clientY, dz.scrollLeft, dz.scrollTop);
    if (dragId.current) { wm.move(dragId.current, col, row); dragId.current = null; return; }
    const p = parsePayload(e.dataTransfer);
    if (p) wm.add(p, col, row);
  };

  const startResize = (w: LayoutWidget) => (e: React.PointerEvent<HTMLDivElement>): void => {
    e.preventDefault(); e.stopPropagation();
    const sx = e.clientX, sy = e.clientY, sc = w.cols, sr = w.rows;
    const mv = (ev: PointerEvent): void => { wm.setSize(w.id, sc + resizeStep(ev.clientX - sx), sr + resizeStep(ev.clientY - sy)); };
    const up = (): void => { document.removeEventListener("pointermove", mv); document.removeEventListener("pointerup", up); };
    document.addEventListener("pointermove", mv); document.addEventListener("pointerup", up);
  };

  return (
    <div
      className="widgetgrid"
      data-dropzone
      onDragOver={(e) => { e.preventDefault(); }}
      onDrop={onDropGrid}
    >
      {wm.widgets.map((w) => {
        let inner: React.JSX.Element;
        if (w.kind === "gauge") {
          inner = <Gauge name={w.name} value={store.latest(w.channelId!) ?? 0} unit={w.unit} scalesOn={scalesOn} />;
        } else if (w.kind === "line") {
          const { xs, ys } = store.series(w.channelId!)?.arrays() ?? { xs: [], ys: [] };
          const xsSec = xs.map((x) => x / 1000);
          inner = <LineChart name={w.name} xs={xsSec} ys={ys} unit={w.unit} value={store.latest(w.channelId!) ?? 0} scalesOn={scalesOn} zoom={w.zoom} onZoomBy={(f) => wm.zoomBy(w.id, f)} onResetZoom={() => wm.resetZoom(w.id)} />;
        } else {
          const { lat, lon } = store.gpsTrack();
          inner = <MapWidget lat={lat} lon={lon} />;
        }
        return (
          <div
            key={w.id}
            className="widget-cell"
            data-widget={w.id}
            draggable
            onDragStart={() => { dragId.current = w.id; }}
            onDragEnd={() => { dragId.current = null; }}
            style={{ gridColumn: `${w.col} / span ${w.cols}`, gridRow: `${w.row} / span ${w.rows}` }}
          >
            <div className="widget-cell-header">
              <span className="widget-cell-grip">☰ {w.name}</span>
              {w.kind !== "map" && (
                <span className="widget-cell-actions">
                  <span data-act="toggle" className="widget-cell-toggle" onClick={() => wm.toggle(w.id)}>
                    {w.kind === "gauge" ? "LINE" : "GAUGE"}
                  </span>
                  <span data-act="remove" className="widget-cell-remove" onClick={() => wm.remove(w.id)}>×</span>
                </span>
              )}
            </div>
            <div className="widget-cell-body">{inner}</div>
            {w.kind !== "map" && (
              <div className="widget-cell-resize" onPointerDown={startResize(w)} aria-label="resize">
                <svg viewBox="0 0 10 10" width="9" height="9"><path d="M9.5 1 L9.5 9.5 L1 9.5" className="widget-cell-resize-glyph" /></svg>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
```

CSS to add/adjust in `theme.css` (theme vars only):

```css
.widgetgrid{display:grid;grid-template-columns:repeat(auto-fill,158px);grid-auto-rows:158px;gap:10px;align-content:start;overflow:auto;height:100%;padding:1px}
.widget-cell{position:relative}
.widget-cell-header{display:flex;align-items:center;justify-content:space-between;gap:5px}
.widget-cell-grip{cursor:move;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.widget-cell-actions{display:flex;align-items:center;gap:3px;flex:none}
.widget-cell-toggle{cursor:pointer;font:600 8px/1 var(--mono);color:var(--accent);border:1px solid var(--border2);border-radius:3px;padding:3px 5px;letter-spacing:.04em}
.widget-cell-remove{cursor:pointer;width:15px;height:15px;display:flex;align-items:center;justify-content:center;color:var(--red);border:1px solid var(--critborder,var(--border2));border-radius:3px;font-size:12px;line-height:1}
.widget-cell-resize{position:absolute;right:0;bottom:0;width:18px;height:18px;cursor:nwse-resize;z-index:6;display:flex;align-items:flex-end;justify-content:flex-end;padding:3px}
.widget-cell-resize-glyph{fill:none;stroke:var(--dim);stroke-width:1.4}
```

(If `--red`/`--border2` are absent in `:root`, the implementer adds them next to the existing palette vars; verify each referenced var exists.)

`LineChart` gains optional props `zoom?: number`, `onZoomBy?`, `onResetZoom?` — defined in Task 8; for THIS task add them to the `LineChart` call but Task 8 wires them. To keep Task 7 compiling before Task 8, the implementer may land Task 8's `LineChart` prop additions first if executing out of order; under subagent-driven order (7 before 8) add the props to `LineChartProps` as optional in this task's `LineChart.tsx` touch, leaving behavior for Task 8. **Simplest:** in Task 7 pass only the props `LineChart` already accepts (`name,xs,ys,unit,value,scalesOn`) and add `zoom`/zoom callbacks in Task 8 along with the wiring. Do the latter — omit `zoom`/`onZoomBy`/`onResetZoom` from the Task-7 `LineChart` call; Task 8 adds them.

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/WidgetGrid.test.tsx && npx tsc --noEmit && npm run build`
Expected: PASS; tsc clean; build OK.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/WidgetGrid.tsx rust/src/ui/app/widgets/WidgetGrid.test.tsx rust/src/ui/app/theme.css
git commit -m "feat(rust-ui): editable WidgetGrid (drag/move/drop/toggle/remove/resize)"
```

---

### Task 8: LineChart — zoom badge + hover crosshair/tooltip

**Files:**
- Modify: `rust/src/ui/app/widgets/LineChart.tsx`
- Modify: `rust/src/ui/app/theme.css` (hover/crosshair/badge classes)
- Test: `rust/src/ui/app/widgets/LineChart.test.tsx` (extend)

**Interfaces:**
- Consumes: extended `lineViz` (Task 3, returns `points`), `hoverInfo` (Task 4).
- Produces: `LineChart` props become `{ name: string; xs: number[]; ys: number[]; unit: string; value: number; scalesOn: boolean; zoom?: number; onZoomBy?: (f: number) => void; onResetZoom?: () => void }`. Internal hover state via `useState`. Right-click is handled by the grid menu (Task 9); LineChart exposes hover only. Renders, when hovering: a vertical crosshair line, a dot at the sample, and a tooltip (`hVal` + `hT`), all from `hoverInfo`. Shows a zoom badge (`×N`) when `zoom>1`.

- [ ] **Step 1: Write the failing test** (extend)

```tsx
// @vitest-environment jsdom
import { fireEvent } from "@testing-library/react";

it("shows a hover tooltip on mouse move and a zoom badge when zoom>1", () => {
  // render <LineChart name="Roll" xs={[0,1,2]} ys={[5,7,9]} unit="deg" value={9} scalesOn zoom={2} />
  expect(screen.getByText("×2")).toBeTruthy();
  const surface = document.querySelector('[data-testid="linechart"] [data-hover-surface]') as HTMLElement;
  fireEvent.mouseMove(surface, { clientX: 9999, clientY: 0 }); // far right → last sample
  // tooltip value text appears (fmtNum(9)=9.000)
  expect(screen.getByText(/9\.000/)).toBeTruthy();
  fireEvent.mouseLeave(surface);
});
```

> jsdom returns a zero-size `getBoundingClientRect`; the hover handler must guard `rect.width === 0 → rel = 0`. The test asserts the last-sample value by passing a large clientX (clamped to rel=1). If width is 0 in jsdom, rel=0 → first sample; to keep the test robust, assert on the tooltip *container* existing after mouseMove rather than a specific sample value, OR have the handler treat `clientX >= rect.right` as rel=1. Implementer: make the assertion match the chosen rel behavior.

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/LineChart.test.tsx`
Expected: FAIL (no badge/hover).

- [ ] **Step 3: Implement** — extend `LineChart.tsx`:
  - Add the three optional props.
  - `const viz = lineViz(xs, ys, value, zoom ?? 1);`
  - `const [hoverRel, setHoverRel] = useState<number | null>(null);`
  - Wrap the SVG in a `<div data-hover-surface onMouseMove onMouseLeave>`; compute `rel = rect.width ? clamp((e.clientX-rect.left)/rect.width,0,1) : 0` (treat `e.clientX>=rect.right` as 1).
  - `const hov = hoverRel == null ? { active: false } as ReturnType<typeof hoverInfo> : hoverInfo(viz.points, hoverRel, unit);`
  - Render crosshair line at `left:hov.hxPct`, dot at `left:hov.hxPct;top:hov.hyPct`, tooltip at `left:hov.tipLeftPct` showing `hov.hVal` + `hov.hT`, only when `hov.active`.
  - Zoom badge `×{zoom}` when `(zoom ?? 1) > 1` (use the cyan `--accent`).
  - Keep the 5 y-labels and value overlay from Phase 3.

CSS (theme vars):

```css
.linechart-hover-surface{position:absolute;inset:6px;cursor:crosshair}
.linechart-crosshair{position:absolute;top:0;bottom:0;width:1px;background:var(--accent);opacity:.55;pointer-events:none}
.linechart-hover-dot{position:absolute;width:6px;height:6px;border-radius:50%;background:var(--accent);border:1px solid var(--bg);transform:translate(-50%,-50%);pointer-events:none;z-index:4}
.linechart-tooltip{position:absolute;top:0;transform:translateX(-50%);pointer-events:none;background:var(--panelhdr);border:1px solid var(--border2);border-radius:3px;padding:2px 5px;font:600 8.5px/1 var(--mono);color:var(--text);white-space:nowrap;z-index:5}
.linechart-zoom-badge{position:absolute;left:14px;top:1px;font:600 9px/1 var(--mono);color:var(--accent);pointer-events:none}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/LineChart.test.tsx && npx tsc --noEmit`
Then wire the props in `WidgetGrid` line branch (add `zoom={w.zoom} onZoomBy={(f)=>wm.zoomBy(w.id,f)} onResetZoom={()=>wm.resetZoom(w.id)}`), run `npm test` + `npm run build`.
Expected: PASS; tsc clean; build OK.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/LineChart.tsx rust/src/ui/app/widgets/LineChart.test.tsx rust/src/ui/app/widgets/WidgetGrid.tsx rust/src/ui/app/theme.css
git commit -m "feat(rust-ui): LineChart hover tooltip + zoom badge"
```

---

### Task 9: Line context-menu (zoom in/out/reset)

**Files:**
- Create: `rust/src/ui/app/widgets/LineMenu.tsx`
- Modify: `rust/src/ui/app/widgets/WidgetGrid.tsx` (menu state + right-click)
- Test: `rust/src/ui/app/widgets/LineMenu.test.tsx` + extend `WidgetGrid.test.tsx`

**Interfaces:**
- Produces:
  - `LineMenu({ x, y, onZoomIn, onZoomOut, onReset }: { x: number; y: number; onZoomIn: () => void; onZoomOut: () => void; onReset: () => void }): React.JSX.Element` — a fixed-position menu (`data-testid="line-menu"`) with three rows: Zoom in (+), Zoom out (−), Reset (×1).
  - WidgetGrid: right-click (`onContextMenu`) on a **line** cell opens the menu at the cursor for that widget; any click closes it. Zoom in = `zoomBy(id,2)`, out = `zoomBy(id,0.5)`, reset = `resetZoom(id)`.

- [ ] **Step 1: Write the failing test**

```tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import { LineMenu } from "./LineMenu";

afterEach(cleanup);

it("renders three actions and fires callbacks", () => {
  const onZoomIn = vi.fn(), onZoomOut = vi.fn(), onReset = vi.fn();
  render(<LineMenu x={10} y={20} onZoomIn={onZoomIn} onZoomOut={onZoomOut} onReset={onReset} />);
  expect(screen.getByTestId("line-menu")).toBeTruthy();
  fireEvent.click(screen.getByText("Zoom in")); expect(onZoomIn).toHaveBeenCalled();
  fireEvent.click(screen.getByText("Zoom out")); expect(onZoomOut).toHaveBeenCalled();
  fireEvent.click(screen.getByText(/Reset/)); expect(onReset).toHaveBeenCalled();
});
```

Plus, in `WidgetGrid.test.tsx`:

```tsx
it("right-click on a line widget opens the zoom menu", () => {
  // toggle the first gauge to a line first (reuse helper from Task 7 test), then:
  const cell = document.querySelector('[data-widget] [data-testid="linechart"]')!.closest("[data-widget]") as HTMLElement;
  fireEvent.contextMenu(cell, { clientX: 50, clientY: 60 });
  expect(screen.getByTestId("line-menu")).toBeTruthy();
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust && npx vitest run src/ui/app/widgets/LineMenu.test.tsx`
Expected: FAIL (module not found).

- [ ] **Step 3: Implement** `LineMenu.tsx`:

```tsx
import type React from "react";

interface LineMenuProps { x: number; y: number; onZoomIn: () => void; onZoomOut: () => void; onReset: () => void }

export function LineMenu({ x, y, onZoomIn, onZoomOut, onReset }: LineMenuProps): React.JSX.Element {
  return (
    <div data-testid="line-menu" className="line-menu" style={{ left: x, top: y }} onClick={(e) => e.stopPropagation()}>
      <div className="line-menu-title">Time axis</div>
      <div className="line-menu-item" onClick={onZoomIn}>Zoom in<span className="line-menu-key">+</span></div>
      <div className="line-menu-item" onClick={onZoomOut}>Zoom out<span className="line-menu-key">−</span></div>
      <div className="line-menu-item line-menu-reset" onClick={onReset}>Reset (×1)</div>
    </div>
  );
}
```

In `WidgetGrid.tsx` add menu state:

```tsx
const [menu, setMenu] = useState<{ id: string; x: number; y: number } | null>(null);
// on each line cell: onContextMenu={(e) => { e.preventDefault(); setMenu({ id: w.id, x: e.clientX, y: e.clientY }); }}
// on the dropzone root: onClick={() => menu && setMenu(null)}
// render when menu:
// {menu && <LineMenu x={menu.x} y={menu.y}
//   onZoomIn={() => { wm.zoomBy(menu.id, 2); setMenu(null); }}
//   onZoomOut={() => { wm.zoomBy(menu.id, 0.5); setMenu(null); }}
//   onReset={() => { wm.resetZoom(menu.id); setMenu(null); }} />}
```

CSS (theme vars):

```css
.line-menu{position:fixed;z-index:200;background:var(--panelhdr);border:1px solid var(--border2);border-radius:6px;padding:4px;box-shadow:0 8px 24px rgba(0,0,0,.55);min-width:132px}
.line-menu-title{padding:3px 10px 5px;font:600 8.5px/1 var(--mono);letter-spacing:.08em;color:var(--dim);text-transform:uppercase}
.line-menu-item{padding:7px 10px;color:var(--text);cursor:pointer;border-radius:4px;font:600 11px/1 var(--mono);display:flex;justify-content:space-between}
.line-menu-item:hover{background:var(--panel2)}
.line-menu-reset{color:var(--title)}
.line-menu-key{color:var(--dim)}
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust && npx vitest run src/ui/app/widgets/LineMenu.test.tsx src/ui/app/widgets/WidgetGrid.test.tsx && npx tsc --noEmit && npm run build`
Expected: PASS; tsc clean; build OK.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/LineMenu.tsx rust/src/ui/app/widgets/LineMenu.test.tsx rust/src/ui/app/widgets/WidgetGrid.tsx rust/src/ui/app/widgets/WidgetGrid.test.tsx rust/src/ui/app/theme.css
git commit -m "feat(rust-ui): line-chart right-click zoom menu"
```

---

### Task 10: Playwright — interactions E2E + baseline

**Files:**
- Create: `rust/e2e/interactions.spec.ts`

**Interfaces:** uses the `?mock=1` deterministic mode (already wired). Mock provides gauge channels + roll/acc_z strips + GPS, so the seeded grid has a map, gauges, and lines.

- [ ] **Step 1: Write the spec**

```ts
// rust/e2e/interactions.spec.ts
import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("toggle a gauge to a line, then remove it", async ({ page }) => {
  const cell = page.locator("[data-widget]").filter({ has: page.getByTestId("gauge") }).first();
  await cell.locator('[data-act="toggle"]').click();
  await expect(cell.getByTestId("linechart")).toBeVisible();
  await cell.locator('[data-act="remove"]').click();
  await expect(cell).toHaveCount(0);
});

test("drag a parameter onto the grid adds a gauge", async ({ page }) => {
  const before = await page.getByTestId("gauge").count();
  const row = page.locator("[data-prow]").first();
  const dz = page.locator("[data-dropzone]");
  await row.dragTo(dz, { targetPosition: { x: 30, y: 30 } });
  await expect(page.getByTestId("gauge")).toHaveCount(before + 1);
});

test("editable grid matches the visual baseline", async ({ page }) => {
  await page.evaluate(() => document.fonts.ready);
  await expect(page.getByTestId("overview-dash")).toHaveScreenshot("interactions.png");
});
```

- [ ] **Step 2: Clean baseline regen** (avoid the Vite-cache trap)

```bash
cd rust
# ensure nothing is serving :1420
rm -rf node_modules/.vite
npm run e2e:update
npm run e2e   # all green
```

- [ ] **Step 3: Verify the baseline is real** — open `rust/e2e/interactions.spec.ts-snapshots/interactions-chromium-win32.png` and confirm the editable grid renders (cells with ☰ headers, toggle/× controls, gauges/lines/map). If blank/wrong, STOP — do not commit a bad baseline.

- [ ] **Step 4: Commit**

```bash
git add rust/e2e/interactions.spec.ts rust/e2e/interactions.spec.ts-snapshots
git commit -m "test(rust-ui): Playwright grid-interactions E2E + baseline"
```

---

### Task 11: Full gate + live verify

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
Confirm on OVERVIEW: drag a param row onto the grid adds a live gauge; drag a widget to move it; the resize handle resizes a line; the header toggle swaps gauge↔line; × removes; right-click a line opens the zoom menu (zoom changes the visible window); hovering a line shows the value/time tooltip. Compare to `docs/sample ui/screenshots/`.

- [ ] **Step 3: Commit any visual tweaks**, then finish the branch (PR).

---

## Self-Review

**Spec coverage:** drag param→gauge (T6 source + T7 drop + T1 `addWidget`), move/reorder (T1 `moveWidget`/`reorderWidgets` + T7 drag), resize (T2 step + T1 `setSize`/`resizeW` + T7 handle), gauge↔line toggle (T1 `toggleType` + T7), remove (T1 + T7), zoom (T1 `zoomBy`/`resetZoom` + T3 window + T9 menu), hover (T3 points + T4 `hoverInfo` + T8), seeding/state (T1 `seedLayout` + T5 `useWidgets`), E2E (T10), gate+live (T11). OSM/Leaflet explicitly deferred to Phase 5 (scope note). ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code. The one cross-task ordering caveat (T7 vs T8 `LineChart` props) is resolved explicitly: T7 omits the zoom props, T8 adds them. ✓

**Type consistency:** `LayoutWidget`/`DragPayload` defined in T1 and consumed unchanged by T5/T6/T7; `lineViz` new signature (T3) consumed by T4 (`points` shape) and T8; `hoverInfo` return shape (T4) consumed by T8; `useWidgets` API (T5) consumed by T7/T8/T9; clamp constants centralized in T1. `cellFromPoint`/`resizeStep` (T2) consumed by T7. ✓
