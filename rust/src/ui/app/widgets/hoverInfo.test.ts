import { describe, it, expect } from "vitest";
import { hoverInfo } from "./hoverInfo";

const pts = [
  { px: 0, yv: 40, val: 5, ts: -9 },
  { px: 100, yv: 20, val: 7, ts: -4.5 },
  { px: 200, yv: 6, val: 9, ts: 0 },
];

describe("hoverInfo", () => {
  it("snaps to nearest sample and reports pcts/value/time", () => {
    const r = hoverInfo(pts, 1, "deg"); // relX 1 → last point
    expect(r.active).toBe(true);
    expect(r.hxPct).toBe("100.00%");
    expect(r.hyPct).toBe("7.50%"); // 6/80
    expect(r.hVal).toBe("9.000 deg");
    expect(r.hT).toBe("-0.0s");
  });
  it("clamps tip left to [20,80]%", () => {
    expect(hoverInfo(pts, 0, "deg").tipLeftPct).toBe("20.00%");
    expect(hoverInfo(pts, 1, "deg").tipLeftPct).toBe("80.00%");
  });
  it("inactive on empty points", () => {
    expect(hoverInfo([], 0.5, "deg").active).toBe(false);
  });
});
