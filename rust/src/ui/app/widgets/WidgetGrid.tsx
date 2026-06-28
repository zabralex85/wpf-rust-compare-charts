import type React from "react";
import { useRef, useState } from "react";
import type { TelemetryStore } from "../../../data/store";
import { useWidgets } from "./useWidgets";
import { cellFromPoint, resizeStep } from "./dropGrid";
import type { DragPayload, LayoutWidget } from "./widgetModel";
import { Gauge } from "./Gauge";
import { LineChart } from "./LineChart";
import { MapWidget } from "./MapWidget";
import { LineMenu } from "./LineMenu";

interface WidgetGridProps { store: TelemetryStore; scalesOn: boolean }

function parsePayload(dt: DataTransfer): DragPayload | null {
  for (const t of ["application/x-inu-param", "text/plain"]) {
    try {
      const raw = dt.getData(t);
      if (raw) {
        const p = JSON.parse(raw) as DragPayload;
        if (typeof p.name === "string") return p;
      }
    } catch { /* ignore */ }
  }
  return null;
}

export function WidgetGrid({ store, scalesOn }: WidgetGridProps): React.JSX.Element {
  const wm = useWidgets(store.channels());
  const dragId = useRef<string | null>(null);
  const resizingRef = useRef(false);
  const [menu, setMenu] = useState<{ id: string; x: number; y: number } | null>(null);

  const onDropGrid = (e: React.DragEvent<HTMLDivElement>): void => {
    e.preventDefault();
    const dz = e.currentTarget;
    const rect = dz.getBoundingClientRect();
    const { col, row } = cellFromPoint(rect, e.clientX, e.clientY, dz.scrollLeft, dz.scrollTop);
    if (dragId.current) {
      wm.move(dragId.current, col, row);
      dragId.current = null;
      return;
    }
    const p = parsePayload(e.dataTransfer);
    if (p) wm.add(p, col, row);
  };

  const startResize = (w: LayoutWidget) => (e: React.PointerEvent<HTMLDivElement>): void => {
    resizingRef.current = true;
    e.preventDefault();
    e.stopPropagation();
    const sx = e.clientX, sy = e.clientY, sc = w.cols, sr = w.rows;
    const mv = (ev: PointerEvent): void => {
      wm.setSize(w.id, sc + resizeStep(ev.clientX - sx), sr + resizeStep(ev.clientY - sy));
    };
    const up = (): void => {
      resizingRef.current = false;
      document.removeEventListener("pointermove", mv);
      document.removeEventListener("pointerup", up);
    };
    document.addEventListener("pointermove", mv);
    document.addEventListener("pointerup", up);
  };

  return (
    <div
      className="widgetgrid"
      data-dropzone
      onDragOver={(e) => { e.preventDefault(); }}
      onDrop={onDropGrid}
      onClick={() => { if (menu) setMenu(null); }}
    >
      {wm.widgets.map((w) => {
        let inner: React.JSX.Element;
        if (w.kind === "gauge") {
          inner = (
            <Gauge
              name={w.name}
              value={store.latest(w.channelId!) ?? 0}
              unit={w.unit}
              scalesOn={scalesOn}
            />
          );
        } else if (w.kind === "line") {
          const { xs, ys } = store.series(w.channelId!)?.arrays() ?? { xs: [], ys: [] };
          const xsSec = xs.map((x) => x / 1000);
          inner = (
            <LineChart
              name={w.name}
              xs={xsSec}
              ys={ys}
              unit={w.unit}
              value={store.latest(w.channelId!) ?? 0}
              scalesOn={scalesOn}
              zoom={w.zoom}
            />
          );
        } else {
          const { lat, lon } = store.gpsTrack();
          inner = <MapWidget lat={lat} lon={lon} />;
        }

        return (
          <div
            key={w.id}
            className="widget-cell"
            data-widget={w.id}
            onContextMenu={w.kind === "line" ? (e) => { e.preventDefault(); setMenu({ id: w.id, x: e.clientX, y: e.clientY }); } : undefined}
            style={{
              gridColumn: `${w.col} / span ${w.cols}`,
              gridRow: `${w.row} / span ${w.rows}`,
            }}
          >
            <div className="widget-cell-header">
              {/* Only the ☰ grip is the drag handle — keeps the toggle/× buttons
                  clickable and leaves the body free for map pan / charts. */}
              <span
                className="widget-cell-grip"
                draggable
                onDragStart={() => { dragId.current = w.id; }}
                onDragEnd={() => { dragId.current = null; }}
              >
                ☰ {w.name}
              </span>
              {w.kind !== "map" && (
                <span className="widget-cell-actions">
                  <span
                    data-act="toggle"
                    className="widget-cell-toggle"
                    onClick={() => { wm.toggle(w.id); }}
                  >
                    {w.kind === "gauge" ? "LINE" : "GAUGE"}
                  </span>
                  <span
                    data-act="remove"
                    className="widget-cell-remove"
                    onClick={() => { wm.remove(w.id); }}
                  >
                    ×
                  </span>
                </span>
              )}
            </div>
            <div className="widget-cell-body">{inner}</div>
            {/* resize handle — all widget kinds (gauge square / line / map free) */}
            <div
              className="widget-cell-resize"
              onPointerDown={startResize(w)}
              aria-label="resize"
            >
              <svg viewBox="0 0 10 10" width="9" height="9">
                <path d="M9.5 1 L9.5 9.5 L1 9.5" className="widget-cell-resize-glyph" />
              </svg>
            </div>
          </div>
        );
      })}
      {menu && (
        <LineMenu
          x={menu.x}
          y={menu.y}
          onZoomIn={() => { wm.zoomBy(menu.id, 2); setMenu(null); }}
          onZoomOut={() => { wm.zoomBy(menu.id, 0.5); setMenu(null); }}
          onReset={() => { wm.resetZoom(menu.id); setMenu(null); }}
        />
      )}
    </div>
  );
}
