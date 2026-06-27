import type React from "react";
import { gaugeViz } from "./gaugeViz";

interface GaugeProps {
  name: string;
  value: number;
  unit: string;
  scalesOn: boolean;
}

export function Gauge({
  name,
  value,
  unit,
  scalesOn,
}: GaugeProps): React.JSX.Element {
  const v = gaugeViz(value);

  // Tick mark positions (5 ticks symmetric about the 270° arc: -135, -90, 0, 90, 135)
  const ticks = [
    { angle: -135, length: 4 },
    { angle: -90, length: 5 },
    { angle: 0, length: 5 },
    { angle: 90, length: 5 },
    { angle: 135, length: 4 },
  ];

  return (
    <div data-testid="gauge" className="gauge-container">
      <svg viewBox="0 0 80 80" className="gauge-svg">
        {/* Face circle */}
        <circle cx="40" cy="40" r="36" className="gauge-face" />

        {/* Arc track (270° from -135 to 135) */}
        <path d="M20.2 59.8 A28 28 0 1 1 59.8 59.8" className="gauge-arc" />

        {/* Tick marks (shown only when scalesOn) */}
        {scalesOn && (
          <g className="gauge-ticks">
            {ticks.map((tick, idx) => {
              const angleRad = (tick.angle * Math.PI) / 180;
              const r1 = 31; // outer radius
              const r2 = r1 - tick.length; // inner radius
              const x1 = 40 + r1 * Math.sin(angleRad);
              const y1 = 40 - r1 * Math.cos(angleRad);
              const x2 = 40 + r2 * Math.sin(angleRad);
              const y2 = 40 - r2 * Math.cos(angleRad);
              return (
                <line
                  key={idx}
                  x1={x1}
                  y1={y1}
                  x2={x2}
                  y2={y2}
                  className="gauge-tick"
                />
              );
            })}
          </g>
        )}

        {/* Needle */}
        <line x1="40" y1="40" x2={v.nx} y2={v.ny} className="gauge-needle" />

        {/* Hub */}
        <circle cx="40" cy="40" r="3" className="gauge-hub" />
      </svg>

      {/* Scale labels (shown only when scalesOn) */}
      {scalesOn && (
        <div className="gauge-labels">
          <div
            className="gauge-scale-label"
            style={{
              position: "absolute",
              left: "18.9%",
              top: "81.1%",
              transform: "translate(-50%, -50%)",
            }}
          >
            {v.gMin}
          </div>
          <div
            className="gauge-scale-label"
            style={{
              position: "absolute",
              left: "9.4%",
              top: "33.2%",
              transform: "translate(-50%, -50%)",
            }}
          >
            {v.gQ1}
          </div>
          <div
            className="gauge-scale-label"
            style={{
              position: "absolute",
              left: "50%",
              top: "6%",
              transform: "translate(-50%, -50%)",
            }}
          >
            0
          </div>
          <div
            className="gauge-scale-label"
            style={{
              position: "absolute",
              left: "90.6%",
              top: "33.2%",
              transform: "translate(-50%, -50%)",
            }}
          >
            {v.gQ3}
          </div>
          <div
            className="gauge-scale-label"
            style={{
              position: "absolute",
              left: "81.1%",
              top: "81.1%",
              transform: "translate(-50%, -50%)",
            }}
          >
            {v.gMax}
          </div>
        </div>
      )}

      {/* Value text and name */}
      <div className="gauge-info">
        <div className="gauge-value">
          {v.valueText} <span className="gauge-unit">{unit}</span>
        </div>
        <div className="gauge-name">{name}</div>
      </div>
    </div>
  );
}
