import { describe, it, expect } from "vitest";
import { lineViz } from "./linePath";

describe("lineViz", () => {
  // ── path shape ───────────────────────────────────────────────────────────

  it("path starts with M", () => {
    const r = lineViz([0, 1, 2], [10, 20, 30]);
    expect(r.path).toMatch(/^M/);
  });

  it("has exactly n coordinate points (count M+L tokens = n)", () => {
    const xs = [0, 1, 2, 3, 4];
    const ys = [1, 2, 3, 4, 5];
    const r = lineViz(xs, ys);
    const count = (r.path.match(/[ML]/g) ?? []).length;
    expect(count).toBe(xs.length);
  });

  // ── exact path for a known input ─────────────────────────────────────────
  // xs=[0,1,2] ys=[0,40,80] viewW=200 viewH=80
  // yMin=0 yMax=80 ySpan=80  topY=6 botY=74 bandH=68
  // y(0)   = 74 - (0/80)*68  = 74.0
  // y(40)  = 74 - (40/80)*68 = 74 - 34 = 40.0
  // y(80)  = 74 - (80/80)*68 = 74 - 68 =  6.0
  // x: i*viewW/(n-1) → 0, 100, 200
  it("produces the exact path for a known 3-point input", () => {
    const r = lineViz([0, 1, 2], [0, 40, 80], 200, 80);
    expect(r.path).toBe("M0.0 74.0L100.0 40.0L200.0 6.0");
  });

  // ── y labels ─────────────────────────────────────────────────────────────

  it("yHi/yMid/yLo use fmtNum and correct range values", () => {
    const r = lineViz([0, 1, 2], [10, 20, 30]);
    expect(r.yHi).toBe("30.000"); // fmtNum(30) → a≥1 → toFixed(3)
    expect(r.yLo).toBe("10.000"); // fmtNum(10)
    expect(r.yMid).toBe("20.000"); // fmtNum((10+30)/2=20)
  });

  // ── x labels ─────────────────────────────────────────────────────────────

  it("x labels: 60 s span → -60s / -30s / 0s (integer format)", () => {
    // xs[0]=0 xs[last]=60  span=60 ≥ 10 → toFixed(0)
    const xs = Array.from({ length: 11 }, (_, i) => i * 6); // 0,6,12,...,60
    const ys = xs.map(() => 1);
    const r = lineViz(xs, ys);
    expect(r.xs0).toBe("-60s");
    expect(r.xMid).toBe("-30s");
    expect(r.xLast).toBe("0s");
  });

  it("x labels: 5 s span → -5.0s / -2.5s / 0s (1-decimal format)", () => {
    // span=5 < 10 → toFixed(1)
    const r = lineViz([0, 2.5, 5], [1, 1, 1]);
    expect(r.xs0).toBe("-5.0s");
    expect(r.xMid).toBe("-2.5s");
    expect(r.xLast).toBe("0s");
  });

  // ── empty series guard ────────────────────────────────────────────────────

  it("empty ys → empty path + em-dash labels", () => {
    const r = lineViz([], []);
    expect(r.path).toBe("");
    expect(r.yHi).toBe("—");
    expect(r.yMid).toBe("—");
    expect(r.yLo).toBe("—");
    expect(r.xs0).toBe("—");
    expect(r.xMid).toBe("—");
    expect(r.xLast).toBe("—");
  });

  // ── single point ──────────────────────────────────────────────────────────

  it("single point → path is M only (no L), x starts at 0", () => {
    const r = lineViz([5], [42]);
    expect(r.path).toMatch(/^M0\.0 /);
    expect(r.path).not.toContain("L");
  });

  // ── flat series ──────────────────────────────────────────────────────────

  it("flat series → no NaN in path, y coords centered in band", () => {
    // ySpan=0 → center y = (6 + 74) / 2 = 40 for default viewH=80
    const r = lineViz([0, 1, 2], [5, 5, 5]);
    expect(r.path).not.toContain("NaN");
    // All three y coords should be the band midpoint (40.0)
    const yCoords = [...r.path.matchAll(/[ML]\S+ (\S+)/g)].map((m) =>
      parseFloat(m[1])
    );
    yCoords.forEach((y) => expect(y).toBeCloseTo(40, 5));
  });
});
