export function toUPlotData(xsMs: number[], ys: number[]): [number[], number[]] {
  const n = Math.min(xsMs.length, ys.length);
  const xs: number[] = [];
  const yy: number[] = [];
  for (let i = 0; i < n; i++) { xs.push(xsMs[i] / 1000); yy.push(ys[i]); }
  return [xs, yy];
}

// x values are already elapsed SECONDS from ride start (ts is ms-from-start),
// so format the axis relative ("m:ss") instead of as a wall-clock time.
export function fmtElapsed(sec: number): string {
  const s = Math.max(0, Math.floor(sec));
  const m = Math.floor(s / 60);
  const r = s % 60;
  return m + ":" + String(r).padStart(2, "0");
}

export function scrollWindow(xsMs: number[], windowMs: number): [number, number] {
  const w = Math.max(1, windowMs);
  if (xsMs.length === 0) return [0, w / 1000];
  const last = xsMs[xsMs.length - 1];
  const first = xsMs[0];
  // Until we have more than a full window of data, anchor the left edge to the
  // first sample so the trace fills the width instead of being pinned to the
  // right of a mostly-empty 60s window. Once the span exceeds the window, scroll.
  const min = Math.max(first, last - w);
  return [min / 1000, last / 1000];
}
