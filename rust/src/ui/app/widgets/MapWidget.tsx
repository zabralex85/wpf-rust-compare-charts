import type React from "react";
import { useId } from "react";
import { projectTrack } from "./mapProj";
import { fmtCoord } from "./geoFormat";

interface MapWidgetProps {
  lat: number[];
  lon: number[];
}

// Default projection view matches mapProj defaults
const VIEW = { x: 60, y: 150, w: 470, h: 340 } as const;

// Centre of the projection view — anchor for range rings and axis lines
const CX = VIEW.x + VIEW.w / 2; // 295
const CY = VIEW.y + VIEW.h / 2; // 320

// Axis tick length (px in SVG units)
const TICK = 8;

export function MapWidget({ lat, lon }: MapWidgetProps): React.JSX.Element {
  const gridId = useId();
  const { path, last } = projectTrack(lat, lon);

  return (
    <div data-testid="mapwidget" className="mapwidget-container">
      {/*
       * ViewBox: "55 145 480 350"
       * Covers (55,145)→(535,495), giving a 5 px margin around the
       * default projection view corners at (60,150) and (530,490).
       */}
      <svg
        viewBox="55 145 480 350"
        preserveAspectRatio="xMidYMid meet"
        className="mapwidget-svg"
      >
        <defs>
          {/* Grid pattern — unique id prevents conflicts when multiple MapWidgets coexist */}
          <pattern
            id={gridId}
            width="40"
            height="40"
            patternUnits="userSpaceOnUse"
          >
            <path
              d="M40 0V40M0 40H40"
              fill="none"
              className="mapwidget-grid-path"
            />
          </pattern>
        </defs>

        {/* Grid fill */}
        <rect
          x={VIEW.x}
          y={VIEW.y}
          width={VIEW.w}
          height={VIEW.h}
          fill={`url(#${gridId})`}
          className="mapwidget-grid-rect"
        />

        {/* Faint cross-hair axis lines through the view centre */}
        <line
          x1={CX}
          y1={VIEW.y}
          x2={CX}
          y2={VIEW.y + VIEW.h}
          className="mapwidget-axis"
        />
        <line
          x1={VIEW.x}
          y1={CY}
          x2={VIEW.x + VIEW.w}
          y2={CY}
          className="mapwidget-axis"
        />

        {/* Decorative range rings — concentric, centred on view centre */}
        <circle cx={CX} cy={CY} r={50} className="mapwidget-ring" />
        <circle cx={CX} cy={CY} r={100} className="mapwidget-ring" />
        <circle
          cx={CX}
          cy={CY}
          r={150}
          className="mapwidget-ring mapwidget-ring-outer"
        />

        {/* Cardinal axis ticks — short strokes at N / S / E / W edges */}
        {/* N */}
        <line
          x1={CX}
          y1={VIEW.y}
          x2={CX}
          y2={VIEW.y + TICK}
          className="mapwidget-tick"
        />
        {/* S */}
        <line
          x1={CX}
          y1={VIEW.y + VIEW.h}
          x2={CX}
          y2={VIEW.y + VIEW.h - TICK}
          className="mapwidget-tick"
        />
        {/* E */}
        <line
          x1={VIEW.x + VIEW.w}
          y1={CY}
          x2={VIEW.x + VIEW.w - TICK}
          y2={CY}
          className="mapwidget-tick"
        />
        {/* W */}
        <line
          x1={VIEW.x}
          y1={CY}
          x2={VIEW.x + TICK}
          y2={CY}
          className="mapwidget-tick"
        />

        {/* Flight track — path is "" when no data, which renders nothing */}
        <path
          d={path}
          fill="none"
          className="mapwidget-track"
          vectorEffect="non-scaling-stroke"
        />

        {/* Live-position marker — circle at the last projected point */}
        {last !== null && (
          <circle
            cx={last.x}
            cy={last.y}
            r={4}
            className="mapwidget-marker"
          />
        )}
      </svg>
      {/* geographic chrome overlays */}
      <div className="mapwidget-compass">N↑</div>
      <div className="mapwidget-coords">{fmtCoord(lat, lon)}</div>
      <div className="mapwidget-scale"><span className="mapwidget-scale-bar" />2 km</div>
    </div>
  );
}
