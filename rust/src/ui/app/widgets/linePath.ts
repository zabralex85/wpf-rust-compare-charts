import { fmtNum } from "./gaugeViz";

const DASH = "—";

export function lineViz(
  xs: number[],
  ys: number[],
  value: number,
  zoom: number = 1,
  viewW: number = 200,
  viewH: number = 80,
): {
  path: string; yHi: string; yQ3: string; yMid: string; yQ1: string; yLo: string;
  xs0: string; xMid: string; xLast: string;
  points: Array<{ px: number; yv: number; val: number; ts: number }>;
} {
  const empty = {
    path: "", yHi: DASH, yQ3: DASH, yMid: DASH, yQ1: DASH, yLo: DASH,
    xs0: DASH, xMid: DASH, xLast: DASH, points: [] as Array<{ px: number; yv: number; val: number; ts: number }>,
  };
  const N = ys.length;
  if (N === 0 || xs.length === 0) return empty;

  const v = value;
  const lr = Math.max(0.5, Math.abs(v) * 0.5);
  const top = 6, bot = viewH - 6, mid = viewH / 2, half = mid - top;

  const z = Math.max(1, zoom);
  const win = Math.min(N, Math.max(1, Math.round(N / z)));
  const start = N - win;
  const lastX = xs[N - 1];

  const points: Array<{ px: number; yv: number; val: number; ts: number }> = [];
  let path = "";
  for (let i = 0; i < win; i++) {
    const val = ys[start + i];
    const yv = Math.min(bot, Math.max(top, mid - ((val - v) / lr) * half));
    const px = win > 1 ? (viewW * i) / (win - 1) : 0;
    path += (i ? "L" : "M") + px.toFixed(1) + " " + yv.toFixed(1);
    points.push({ px, yv, val, ts: xs[start + i] - lastX });
  }

  const span = lastX - xs[start];
  const fts = (s: number): string => (s < 10 ? s.toFixed(1) : s.toFixed(0));
  const xs0 = "-" + fts(span) + "s";
  const xMid = "-" + fts(span / 2) + "s";
  const xLast = "0s";

  return {
    path,
    yHi: fmtNum(v + lr), yQ3: fmtNum(v + lr / 2), yMid: fmtNum(v), yQ1: fmtNum(v - lr / 2), yLo: fmtNum(v - lr),
    xs0, xMid, xLast, points,
  };
}
