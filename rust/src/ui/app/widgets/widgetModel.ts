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
