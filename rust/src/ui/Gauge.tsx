import React from "react";
import { gaugeAngle } from "../gauge/geometry";

export function Gauge({ label, unit, value, min, max }: { label: string; unit: string; value: number; min: number; max: number }): React.JSX.Element {
  const angle = gaugeAngle(value, min, max, -120, 120);
  return (
    <div className="gauge">
      <svg viewBox="-50 -50 100 100" width="120" height="120">
        <circle cx="0" cy="0" r="46" className="gauge-face" />
        <g data-testid="needle" transform={`rotate(${angle})`}>
          <line x1="0" y1="0" x2="0" y2="-38" className="gauge-needle" />
        </g>
        <circle cx="0" cy="0" r="3" className="gauge-hub" />
        <text x="0" y="30" textAnchor="middle" className="gauge-label">{label}</text>
        <text x="0" y="42" textAnchor="middle" className="gauge-value">{value.toFixed(2)} {unit}</text>
      </svg>
    </div>
  );
}
