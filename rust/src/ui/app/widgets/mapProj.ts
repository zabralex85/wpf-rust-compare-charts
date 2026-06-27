/** Fraction of each dimension used as inner padding on both sides (6%). */
const PAD_FRAC = 0.06;

export interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface MapViz {
  path: string;
  last: { x: number; y: number } | null;
}

const EMPTY_RESULT: MapViz = { path: "", last: null };

/**
 * Project a GPS track (lat/lon arrays) into an SVG viewport box.
 *
 * Mapping:
 *   lon → x: min lon → left edge (view.x + pad), max lon → right edge.
 *   lat → y (INVERTED): max lat → top edge (view.y + pad), min lat → bottom.
 *   Geographic convention: north / higher latitude is up (smaller SVG y).
 *
 * A uniform PAD_FRAC (6%) inset is applied on each side so the track
 * does not touch the viewport edges.
 *
 * Degenerate ranges (zero span → no divide-by-zero):
 *   - All lats equal → all y coords map to the vertical centre of the inner box.
 *   - All lons equal → all x coords map to the horizontal centre of the inner box.
 *   - Single point  → both spans are zero; maps to the inner-box centre.
 *
 * @param lat   Latitude array (degrees).
 * @param lon   Longitude array (degrees), co-indexed with lat (same length assumed).
 * @param view  SVG viewport box (default { x: 60, y: 150, w: 470, h: 340 }).
 *
 * @returns
 *   path – SVG path "M{x} {y}L{x} {y}…" with coords to 1 decimal; "" when empty.
 *   last – Last projected point {x, y} as raw numbers for the live-position marker;
 *           null when input is empty.
 */
export function projectTrack(
  lat: number[],
  lon: number[],
  view: ViewBox = { x: 60, y: 150, w: 470, h: 340 },
): MapViz {
  if (lat.length === 0 || lon.length === 0) {
    return { ...EMPTY_RESULT };
  }

  const n = lat.length;

  // ── Inner box (padded) ───────────────────────────────────────────────────
  const padX = view.w * PAD_FRAC;
  const padY = view.h * PAD_FRAC;
  const innerLeft  = view.x + padX;
  const innerRight = view.x + view.w - padX;
  const innerTop   = view.y + padY;
  const innerBot   = view.y + view.h - padY;

  const centerX = (innerLeft  + innerRight) / 2;
  const centerY = (innerTop   + innerBot)   / 2;

  // ── Data bounding box ────────────────────────────────────────────────────
  const minLat = Math.min(...lat);
  const maxLat = Math.max(...lat);
  const latSpan = maxLat - minLat;

  const minLon = Math.min(...lon);
  const maxLon = Math.max(...lon);
  const lonSpan = maxLon - minLon;

  // ── Projection helpers ───────────────────────────────────────────────────
  const mapX = (lo: number): number => {
    if (lonSpan === 0) return centerX; // degenerate: centre horizontally
    return innerLeft + ((lo - minLon) / lonSpan) * (innerRight - innerLeft);
  };

  const mapY = (la: number): number => {
    if (latSpan === 0) return centerY; // degenerate: centre vertically
    // Invert: higher lat → smaller SVG y (north is up)
    return innerBot - ((la - minLat) / latSpan) * (innerBot - innerTop);
  };

  // ── Build SVG path ────────────────────────────────────────────────────────
  let path = "";
  let lastPt: { x: number; y: number } | null = null;

  for (let i = 0; i < n; i++) {
    const px = mapX(lon[i]);
    const py = mapY(lat[i]);
    path += (i === 0 ? "M" : "L") + px.toFixed(1) + " " + py.toFixed(1);
    lastPt = { x: px, y: py };
  }

  return { path, last: lastPt };
}
