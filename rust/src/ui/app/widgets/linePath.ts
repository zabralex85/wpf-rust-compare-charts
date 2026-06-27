import { fmtNum } from "./gaugeViz";

export interface LineViz {
  path: string;
  yHi: string;
  yQ3: string;
  yMid: string;
  yQ1: string;
  yLo: string;
  xs0: string;
  xMid: string;
  xLast: string;
}

const DASH = "—";

const EMPTY_RESULT: LineViz = {
  path: "",
  yHi: DASH,
  yQ3: DASH,
  yMid: DASH,
  yQ1: DASH,
  yLo: DASH,
  xs0: DASH,
  xMid: DASH,
  xLast: DASH,
};

/**
 * Format a time span in seconds for x-axis labels.
 * ≥10 → integer (toFixed(0)), <10 → 1 decimal (toFixed(1)).
 */
function fmtSpan(s: number): string {
  return s >= 10 ? s.toFixed(0) : s.toFixed(1);
}

/**
 * Compute SVG line-chart path and axis labels from raw telemetry arrays.
 * Y-axis is centered on `value` with a ±lr band (lr = max(0.5, |value|*0.5)).
 *
 * @param xs    - Time values in seconds (used only for x-axis label span; points are
 *               index-mapped to pixels, not value-mapped).
 * @param ys    - Data values to render; index-aligned to xs.
 * @param value - Current channel value; the chart centres on this.
 * @param viewW - SVG viewport width in px (default 200).
 * @param viewH - SVG viewport height in px (default 80).
 *
 * @returns
 *   path   – SVG path string "M{x} {y}L{x} {y}…" with coords to 1 decimal.
 *   yHi    – fmtNum(value + lr)
 *   yQ3    – fmtNum(value + lr/2)
 *   yMid   – fmtNum(value)
 *   yQ1    – fmtNum(value - lr/2)
 *   yLo    – fmtNum(value - lr)
 *   xs0    – leftmost x label "-{span}s" (span = xs[last]−xs[0])
 *   xMid   – middle x label "-{span/2}s"
 *   xLast  – rightmost x label "0s"
 *
 * Empty xs/ys → path "" and all labels "—".
 * Single point → "M0.0 {y}" with no L segment.
 * Values outside the band clamp to top/bottom pixel rows.
 */
export function lineViz(
  xs: number[],
  ys: number[],
  value: number,
  viewW = 200,
  viewH = 80,
): LineViz {
  if (ys.length === 0 || xs.length === 0) {
    return { ...EMPTY_RESULT };
  }

  const n = ys.length;
  const v = value;
  const lr = Math.max(0.5, Math.abs(v) * 0.5);

  // Band geometry (for viewH=80: top=6, bot=74, mid=40, half=34)
  const top = 6;
  const bot = viewH - 6;
  const mid = viewH / 2;
  const half = mid - top;

  // ── Y mapping: value-centered, inverted (larger value → smaller y) ────────
  const clamp = (val: number, lo: number, hi: number): number =>
    Math.max(lo, Math.min(hi, val));

  const mapY = (yv: number): number =>
    clamp(mid - ((yv - v) / lr) * half, top, bot);

  // ── X mapping (index-based, not value-based) ─────────────────────────────
  const mapX = (i: number): number => (n === 1 ? 0 : (viewW * i) / (n - 1));

  // ── Build SVG path ────────────────────────────────────────────────────────
  let path = "";
  for (let i = 0; i < n; i++) {
    const px = mapX(i).toFixed(1);
    const py = mapY(ys[i]).toFixed(1);
    path += (i === 0 ? "M" : "L") + px + " " + py;
  }

  // ── Y labels (5, value-centred) ───────────────────────────────────────────
  const yHi = fmtNum(v + lr);
  const yQ3 = fmtNum(v + lr / 2);
  const yMid = fmtNum(v);
  const yQ1 = fmtNum(v - lr / 2);
  const yLo = fmtNum(v - lr);

  // ── X labels (span derived from xs time values) ──────────────────────────
  const span = xs[xs.length - 1] - xs[0];
  const xs0 = "-" + fmtSpan(span) + "s";
  const xMid = "-" + fmtSpan(span / 2) + "s";
  const xLast = "0s";

  return { path, yHi, yQ3, yMid, yQ1, yLo, xs0, xMid, xLast };
}
