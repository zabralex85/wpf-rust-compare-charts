import { fmtNum } from "./gaugeViz";

export interface LineViz {
  path: string;
  yHi: string;
  yMid: string;
  yLo: string;
  xs0: string;
  xMid: string;
  xLast: string;
}

const DASH = "—";

const EMPTY_RESULT: LineViz = {
  path: "",
  yHi: DASH,
  yMid: DASH,
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
 *
 * @param xs  - Time values in seconds (used only for x-axis label span; points are
 *              index-mapped to pixels, not value-mapped).
 * @param ys  - Data values to render; index-aligned to xs.
 * @param viewW - SVG viewport width in px (default 200).
 * @param viewH - SVG viewport height in px (default 80).
 *
 * @returns
 *   path   – SVG path string "M{x} {y}L{x} {y}…" with coords to 1 decimal.
 *   yHi    – fmtNum(max ys)
 *   yMid   – fmtNum((min+max)/2)
 *   yLo    – fmtNum(min ys)
 *   xs0    – leftmost x label "-{span}s" (span = xs[last]−xs[0])
 *   xMid   – middle x label "-{span/2}s"
 *   xLast  – rightmost x label "0s"
 *
 * Empty xs/ys → path "" and all labels "—".
 * Flat ys (all equal) → y coords centered at band midpoint (no NaN).
 * Single point → "M0.0 {y}" with no L segment.
 */
export function lineViz(
  xs: number[],
  ys: number[],
  viewW = 200,
  viewH = 80,
): LineViz {
  if (ys.length === 0 || xs.length === 0) {
    return { ...EMPTY_RESULT };
  }

  const n = ys.length;

  // ── Y mapping ────────────────────────────────────────────────────────────
  const yMin = Math.min(...ys);
  const yMax = Math.max(...ys);
  const ySpan = yMax - yMin;

  const topY = 6; // px: high values map here (inverted)
  const botY = viewH - 6; // px: low values map here
  const bandH = botY - topY;

  const mapY = (v: number): number => {
    if (ySpan === 0) {
      // Flat series: center in band, avoid divide-by-zero
      return (topY + botY) / 2;
    }
    // Invert: larger value → smaller y (closer to top)
    return botY - ((v - yMin) / ySpan) * bandH;
  };

  // ── X mapping (index-based, not value-based) ─────────────────────────────
  const mapX = (i: number): number => {
    if (n === 1) return 0;
    return (viewW * i) / (n - 1);
  };

  // ── Build SVG path ────────────────────────────────────────────────────────
  let path = "";
  for (let i = 0; i < n; i++) {
    const px = mapX(i).toFixed(1);
    const py = mapY(ys[i]).toFixed(1);
    path += (i === 0 ? "M" : "L") + px + " " + py;
  }

  // ── Y labels ─────────────────────────────────────────────────────────────
  const yHi = fmtNum(yMax);
  const yMid = fmtNum((yMin + yMax) / 2);
  const yLo = fmtNum(yMin);

  // ── X labels (span derived from xs time values) ──────────────────────────
  const span = xs[xs.length - 1] - xs[0];
  const xs0 = "-" + fmtSpan(span) + "s";
  const xMid = "-" + fmtSpan(span / 2) + "s";
  const xLast = "0s";

  return { path, yHi, yMid, yLo, xs0, xMid, xLast };
}
