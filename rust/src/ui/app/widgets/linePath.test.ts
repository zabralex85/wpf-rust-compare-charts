import { describe, it, expect } from "vitest";
import { lineViz } from "./linePath";
import { fmtNum } from "./gaugeViz";

describe("lineViz", () => {
  // ── path shape ───────────────────────────────────────────────────────────

  it("path starts with M", () => {
    const r = lineViz([0, 1, 2], [10, 20, 30], 20);
    expect(r.path).toMatch(/^M/);
  });

  it("has exactly n coordinate points (count M+L tokens = n)", () => {
    const xs = [0, 1, 2, 3, 4];
    const ys = [1, 2, 3, 4, 5];
    const r = lineViz(xs, ys, 3);
    const count = (r.path.match(/[ML]/g) ?? []).length;
    expect(count).toBe(xs.length);
  });

  // ── exact path for a known input (value-centered) ────────────────────────
  // xs=[0,1,2] ys=[10,15,0] value=10 viewW=200 viewH=80
  // v=10 lr=max(0.5,5)=5  top=6 bot=74 mid=40 half=34
  // i=0: ys=10  y=clamp(40-(10-10)/5*34,6,74)=40       x=0   → M0.0 40.0
  // i=1: ys=15  y=clamp(40-(15-10)/5*34,6,74)=clamp(6,6,74)=6  x=100 → L100.0 6.0
  // i=2: ys=0   y=clamp(40-(0-10)/5*34,6,74)=clamp(108,6,74)=74 x=200 → L200.0 74.0
  it("produces the exact path for a known 3-point input", () => {
    const r = lineViz([0, 1, 2], [10, 15, 0], 10, 200, 80);
    expect(r.path).toBe("M0.0 40.0L100.0 6.0L200.0 74.0");
  });

  // ── value-centered y mapping ──────────────────────────────────────────────

  it("point at value maps to mid y (40) for default viewH=80", () => {
    // v=10 lr=5 mid=40; ys[0]=10 → y=40
    const r = lineViz([0], [10], 10, 200, 80);
    expect(r.path).toMatch(/^M0\.0 40\.0$/);
  });

  it("point at value+lr maps to top y (6) for default viewH=80", () => {
    // v=10 lr=5; ys=15=v+lr → y=6
    const r = lineViz([0, 1], [15, 15], 10, 200, 80);
    const yCoords = [...r.path.matchAll(/[ML]\S+ (\S+)/g)].map((m) =>
      parseFloat(m[1])
    );
    yCoords.forEach((y) => expect(y).toBeCloseTo(6, 5));
  });

  it("ys far above value clamp to top (6)", () => {
    // v=10 lr=5; ys=999 → clamped to 6
    const r = lineViz([0, 1], [999, -999], 10, 200, 80);
    expect(r.path).toBe("M0.0 6.0L200.0 74.0");
  });

  // ── 5 y-labels ───────────────────────────────────────────────────────────

  it("5 y-labels correct for v=10 (lr=5)", () => {
    // v=10 → lr=max(0.5,5)=5
    const r = lineViz([0, 1], [10, 10], 10);
    expect(r.yHi).toBe(fmtNum(15));   // "15.000"
    expect(r.yQ3).toBe(fmtNum(12.5)); // "12.500"
    expect(r.yMid).toBe(fmtNum(10));  // "10.000"
    expect(r.yQ1).toBe(fmtNum(7.5));  // "7.500"
    expect(r.yLo).toBe(fmtNum(5));    // "5.000"
  });

  it("5 y-labels use lr floor of 0.5 when value=0", () => {
    // v=0 → lr=max(0.5,0)=0.5
    const r = lineViz([0, 1], [0, 0], 0);
    expect(r.yHi).toBe(fmtNum(0.5));
    expect(r.yQ3).toBe(fmtNum(0.25));
    expect(r.yMid).toBe(fmtNum(0));
    expect(r.yQ1).toBe(fmtNum(-0.25));
    expect(r.yLo).toBe(fmtNum(-0.5));
  });

  // ── x labels ─────────────────────────────────────────────────────────────

  it("x labels: 60 s span → -60s / -30s / 0s (integer format)", () => {
    // xs[0]=0 xs[last]=60  span=60 ≥ 10 → toFixed(0)
    const xs = Array.from({ length: 11 }, (_, i) => i * 6); // 0,6,12,...,60
    const ys = xs.map(() => 1);
    const r = lineViz(xs, ys, 1);
    expect(r.xs0).toBe("-60s");
    expect(r.xMid).toBe("-30s");
    expect(r.xLast).toBe("0s");
  });

  it("x labels: 5 s span → -5.0s / -2.5s / 0s (1-decimal format)", () => {
    // span=5 < 10 → toFixed(1)
    const r = lineViz([0, 2.5, 5], [1, 1, 1], 1);
    expect(r.xs0).toBe("-5.0s");
    expect(r.xMid).toBe("-2.5s");
    expect(r.xLast).toBe("0s");
  });

  // ── empty series guard ────────────────────────────────────────────────────

  it("empty ys → empty path + em-dash labels (all 5 y-labels and x-labels)", () => {
    const r = lineViz([], [], 0);
    expect(r.path).toBe("");
    expect(r.yHi).toBe("—");
    expect(r.yQ3).toBe("—");
    expect(r.yMid).toBe("—");
    expect(r.yQ1).toBe("—");
    expect(r.yLo).toBe("—");
    expect(r.xs0).toBe("—");
    expect(r.xMid).toBe("—");
    expect(r.xLast).toBe("—");
  });

  // ── single point ──────────────────────────────────────────────────────────

  it("single point → path is M only (no L), x starts at 0", () => {
    const r = lineViz([5], [10], 10);
    expect(r.path).toMatch(/^M0\.0 /);
    expect(r.path).not.toContain("L");
  });

  // ── no NaN in path ───────────────────────────────────────────────────────

  it("no NaN in path when ys equals value (all map to mid)", () => {
    // v=5, ys all equal value → all y=40 (mid), no NaN
    const r = lineViz([0, 1, 2], [5, 5, 5], 5);
    expect(r.path).not.toContain("NaN");
    const yCoords = [...r.path.matchAll(/[ML]\S+ (\S+)/g)].map((m) =>
      parseFloat(m[1])
    );
    yCoords.forEach((y) => expect(y).toBeCloseTo(40, 5));
  });
});
