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
    // NOTE: not yet wired into WidgetGrid (reserved for a future reorder/relative-resize interaction); covered by unit tests.
    case "reorder": return { ...s, widgets: reorderWidgets(s.widgets, a.draggedId, a.targetId) };
    case "setSize": return { ...s, widgets: modelSetSize(s.widgets, a.id, a.cols, a.rows) };
    // NOTE: not yet wired into WidgetGrid (reserved for a future reorder/relative-resize interaction); covered by unit tests.
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
