import type React from "react";
import { useId } from "react";
import { lineViz } from "./linePath";
import { fmtNum } from "./gaugeViz";

interface LineChartProps {
  name: string;
  xs: number[];
  ys: number[];
  unit: string;
  value: number;
  scalesOn: boolean;
}

export function LineChart({
  name,
  xs,
  ys,
  unit,
  value,
  scalesOn,
}: LineChartProps): React.JSX.Element {
  const gridId = useId();
  const viz = lineViz(xs, ys, value);

  return (
    <div data-testid="linechart" className="linechart-container">
      <div className="linechart-chart">
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
