export function toUPlotData(xsMs: number[], ys: number[]): [number[], number[]] {
  const n = Math.min(xsMs.length, ys.length);
  const xs: number[] = [];
  const yy: number[] = [];
  for (let i = 0; i < n; i++) { xs.push(xsMs[i] / 1000); yy.push(ys[i]); }
  return [xs, yy];
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
