import type React from "react";
import { useId, useState } from "react";
import { lineViz } from "./linePath";
import { hoverInfo } from "./hoverInfo";
import { fmtNum } from "./gaugeViz";

interface LineChartProps {
  name: string;
  xs: number[];
  ys: number[];
  unit: string;
  value: number;
  scalesOn: boolean;
  zoom?: number;
  onZoomBy?: (f: number) => void;
  onResetZoom?: () => void;
}

export function LineChart({
  name,
  xs,
  ys,
  unit,
  value,
  scalesOn,
  zoom,
  onZoomBy: _onZoomBy,
  onResetZoom: _onResetZoom,
}: LineChartProps): React.JSX.Element {
  const gridId = useId();
  const [hoverRel, setHoverRel] = useState<number | null>(null);
  const viz = lineViz(xs, ys, value, zoom ?? 1);
  const hov = hoverRel == null ? null : hoverInfo(viz.points, hoverRel, unit);

  const onMouseMove = (e: React.MouseEvent<HTMLDivElement>): void => {
    const r = e.currentTarget.getBoundingClientRect();
    const rel = r.width ? Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) : 0;
    setHoverRel(e.clientX >= r.right ? 1 : rel);
  };

  return (
    <div data-testid="linechart" className="linechart-container">
      {(zoom ?? 1) > 1 && (
        <span className="linechart-zoom-badge">×{zoom}</span>
      )}

      <div className="linechart-chart">
        <div
          data-hover-surface
          className="linechart-hover-surface"
          onMouseMove={onMouseMove}
          onMouseLeave={() => { setHoverRel(null); }}
        >
          <svg
            viewBox="0 0 200 80"
            preserveAspectRatio="none"
            className="linechart-svg"
          >
            <defs>
              <pattern
                id={gridId}
                width="20"
                height="13.33"
                patternUnits="userSpaceOnUse"
              >
                <path
                  d="M20 0V13.33M0 13.33H20"
                  fill="none"
                  className="linechart-grid-path"
                />
              </pattern>
            </defs>

            {/* Grid overlay — gated by scalesOn */}
            {scalesOn && (
              <rect
                x="0"
                y="0"
                width="200"
                height="80"
                fill={`url(#${gridId})`}
                className="linechart-grid-rect"
              />
            )}

            {/* Mid dashed line at y=40 (always visible) */}
            <line
              x1="0"
              y1="40"
              x2="200"
              y2="40"
              className="linechart-mid-line"
            />

            {/* Data path */}
            <path
              d={viz.path}
              fill="none"
              className="linechart-path"
              vectorEffect="non-scaling-stroke"
            />
          </svg>

          {/* Hover crosshair, dot, tooltip — rendered inside the hover surface */}
          {hov?.active && (
            <>
              <div
                className="linechart-crosshair"
                style={{ left: hov.hxPct }}
              />
              <div
                className="linechart-hover-dot"
                style={{ left: hov.hxPct, top: hov.hyPct }}
              />
              <div
                className="linechart-tooltip"
                style={{ left: hov.tipLeftPct }}
              >
                {hov.hVal}
                <span className="dim"> {hov.hT}</span>
              </div>
            </>
          )}
        </div>

        {/* Value + unit — top-right overlay */}
        <div className="linechart-value-overlay">
          {fmtNum(value)}
          <span className="linechart-unit"> {unit}</span>
        </div>

        {/* Y-axis labels — left edge, 5 value-centered labels, shown only when scalesOn */}
        {scalesOn && (
          <div className="linechart-y-labels">
            <span className="linechart-scale-label">{viz.yHi}</span>
            <span className="linechart-scale-label">{viz.yQ3}</span>
            <span className="linechart-scale-label">{viz.yMid}</span>
            <span className="linechart-scale-label">{viz.yQ1}</span>
            <span className="linechart-scale-label">{viz.yLo}</span>
          </div>
        )}

        {/* X-axis labels — bottom edge, shown only when scalesOn */}
        {scalesOn && (
          <div className="linechart-x-labels">
            <span className="linechart-scale-label">{viz.xs0}</span>
            <span className="linechart-scale-label">{viz.xMid}</span>
            <span className="linechart-scale-label">{viz.xLast}</span>
          </div>
        )}
      </div>

      {/* Channel name — below chart, mirrors Gauge's gauge-name placement */}
      <div className="linechart-name">{name}</div>
    </div>
  );
}
