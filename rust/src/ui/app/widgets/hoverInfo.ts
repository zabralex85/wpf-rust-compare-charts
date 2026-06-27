import { fmtNum } from "./gaugeViz";

export function hoverInfo(
  points: Array<{ px: number; yv: number; val: number; ts: number }>,
  relX: number,
  unit: string,
  viewW: number = 200,
  viewH: number = 80,
): { active: boolean; hxPct: string; hyPct: string; hVal: string; hT: string; tipLeftPct: string } {
  if (points.length === 0) {
    return { active: false, hxPct: "0%", hyPct: "0%", hVal: "", hT: "", tipLeftPct: "50%" };
  }
  const idx = Math.round(Math.min(1, Math.max(0, relX)) * (points.length - 1));
  const p = points[Math.min(points.length - 1, Math.max(0, idx))];
  const at = Math.abs(p.ts);
  return {
    active: true,
    hxPct: ((p.px / viewW) * 100).toFixed(2) + "%",
    hyPct: ((p.yv / viewH) * 100).toFixed(2) + "%",
    hVal: fmtNum(p.val) + (unit ? " " + unit : ""),
    hT: "-" + (at < 10 ? at.toFixed(1) : at.toFixed(0)) + "s",
    tipLeftPct: Math.max(20, Math.min(80, (p.px / viewW) * 100)).toFixed(2) + "%",
  };
}
