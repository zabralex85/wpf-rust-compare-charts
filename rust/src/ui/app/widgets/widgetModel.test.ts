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

describe("setSize map", () => {
  it("clamps a map to cols[2,8]/rows[2,6], free aspect", () => {
    const m = (over = {}) => ({ id: "m", kind: "map" as const, name: "Map", unit: "", cols: 4, rows: 4, col: 1, row: 1, zoom: 1, ...over });
    expect(setSize([m()], "m", 99, 99)[0]).toMatchObject({ cols: 8, rows: 6 });
    expect(setSize([m()], "m", 1, 1)[0]).toMatchObject({ cols: 2, rows: 2 });
    expect(setSize([m()], "m", 5, 3)[0]).toMatchObject({ cols: 5, rows: 3 }); // not forced square
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
