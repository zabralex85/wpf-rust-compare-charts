/**
 * Format a number for display (value text).
 * - If not finite → "—"
 * - a = |v|
 * - a >= 100 → toFixed(1)
 * - a >= 1 → toFixed(3)
 * - else → toFixed(6)
 */
export function fmtNum(v: number): string {
  if (!isFinite(v)) {
    return "—";
  }
  const a = Math.abs(v);
  if (a >= 100) {
    return v.toFixed(1);
  } else if (a >= 1) {
    return v.toFixed(3);
  } else {
    return v.toFixed(6);
  }
}

/**
 * Format a scale value.
 * - If v === 0 → "0"
 * - a = |v|
 * - a >= 100 → toFixed(0)
 * - a >= 1 → toFixed(1)
 * - a >= 0.1 → toFixed(2)
 * - else → toFixed(3)
 * - Strip trailing zeros and trailing "."
 */
export function fmtScale(v: number): string {
  if (v === 0) {
    return "0";
  }
  const a = Math.abs(v);
  let result: string;
  if (a >= 100) {
    result = v.toFixed(0);
  } else if (a >= 1) {
    result = v.toFixed(1);
  } else if (a >= 0.1) {
    result = v.toFixed(2);
  } else {
    result = v.toFixed(3);
  }
  // Strip trailing zeros and trailing "."
  result = result.replace(/\.?0+$/, "");
  return result;
}

/**
 * Compute gauge visualization: needle angle, position, and scale labels.
 */
export interface GaugeViz {
  angleDeg: number;
  nx: number;
  ny: number;
  gMin: string;
  gQ1: string;
  gQ3: string;
  gMax: string;
  valueText: string;
}

export function gaugeViz(value: number): GaugeViz {
  // Step 1: Auto-scale
  const raw = Math.max(Math.abs(value) * 1.3, 1e-6);
  const ex = Math.floor(Math.log10(raw));
  const ff = raw / Math.pow(10, ex);

  // Round to nice scale factor
  let nf: number;
  if (ff <= 1) {
    nf = 1;
  } else if (ff <= 2) {
    nf = 2;
  } else if (ff <= 2.5) {
    nf = 2.5;
  } else if (ff <= 5) {
    nf = 5;
  } else {
    nf = 10;
  }

  const R = nf * Math.pow(10, ex);

  // Step 2: Compute needle angle
  const frac = Math.max(0, Math.min(1, (value + R) / (2 * R)));
  const angleDeg = -135 + frac * 270;
  const ang_rad = (angleDeg * Math.PI) / 180;

  // Step 3: Compute needle position
  const nx = 40 + 28 * Math.sin(ang_rad);
  const ny = 40 - 28 * Math.cos(ang_rad);

  // Step 4: Format scale labels
  const gMin = fmtScale(-R);
  const gQ1 = fmtScale(-R / 2);
  const gQ3 = fmtScale(R / 2);
  const gMax = fmtScale(R);

  // Step 5: Format value text
  const valueText = fmtNum(value);

  return {
    angleDeg,
    nx,
    ny,
    gMin,
    gQ1,
    gQ3,
    gMax,
    valueText,
  };
}
