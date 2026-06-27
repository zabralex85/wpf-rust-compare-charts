import { describe, it, expect } from "vitest";
import { projectTrack } from "./mapProj";

describe("projectTrack", () => {
  const DEFAULT_VIEW = { x: 60, y: 150, w: 470, h: 340 };

  // ── empty guard ──────────────────────────────────────────────────────────
  it("empty arrays → path '' and last null", () => {
    const r = projectTrack([], []);
    expect(r.path).toBe("");
    expect(r.last).toBeNull();
  });

  // ── 2 points → M…L…, last within view box ────────────────────────────────
  it("2 points → path has exactly 2 coord tokens (M + L), last within view box", () => {
    const r = projectTrack([40, 41], [-74, -73]);
    const count = (r.path.match(/[ML]/g) ?? []).length;
    expect(count).toBe(2);
    expect(r.path).toMatch(/^M/);
    expect(r.path).toContain("L");
    expect(r.last).not.toBeNull();
    expect(r.last!.x).toBeGreaterThanOrEqual(DEFAULT_VIEW.x);
    expect(r.last!.x).toBeLessThanOrEqual(DEFAULT_VIEW.x + DEFAULT_VIEW.w);
    expect(r.last!.y).toBeGreaterThanOrEqual(DEFAULT_VIEW.y);
    expect(r.last!.y).toBeLessThanOrEqual(DEFAULT_VIEW.y + DEFAULT_VIEW.h);
  });

  // ── lat inversion ────────────────────────────────────────────────────────
  // Higher-lat point must have SMALLER svg y (north is up)
  it("higher-lat point has smaller y (north is up)", () => {
    // lat[0]=40 (south), lat[1]=41 (north) → y[0] > y[1]
    const r = projectTrack([40, 41], [-74, -73]);
    // Use numeric-specific pattern so it doesn't greedily consume the next M/L token
    const ys = [...r.path.matchAll(/[ML]-?[\d.]+ (-?[\d.]+)/g)].map((m) =>
      parseFloat(m[1]),
    );
    expect(ys).toHaveLength(2);
    expect(ys[0]).toBeGreaterThan(ys[1]);
  });

  // ── exact path for a known 2-point input ─────────────────────────────────
  // view={x:0,y:0,w:100,h:100}, PAD_FRAC=0.06 → pad≈6, inner=[6,94]×[6,94]
  // lat=[0,1], lon=[0,1]:
  //   point 0 (lat=0/south, lon=0/west): x=innerLeft≈6 → "6.0", y=innerBot≈94 → "94.0"
  //   point 1 (lat=1/north, lon=1/east): x=innerRight≈94 → "94.0", y=innerTop≈6 → "6.0"
  it("produces the exact path for a known 2-point input (PAD_FRAC=0.06 → pad≈6 on 100×100 view)", () => {
    const view = { x: 0, y: 0, w: 100, h: 100 };
    const r = projectTrack([0, 1], [0, 1], view);
    expect(r.path).toBe("M6.0 94.0L94.0 6.0");
    expect(r.last).not.toBeNull();
    expect(r.last!.x).toBeCloseTo(94, 1);
    expect(r.last!.y).toBeCloseTo(6, 1);
  });

  // ── single point ──────────────────────────────────────────────────────────
  // lat/lon spans both 0 → center both axes; no divide-by-zero, no NaN
  it("single point → M only (no L), last within box, no NaN", () => {
    const r = projectTrack([40], [-74]);
    expect(r.path).toMatch(/^M/);
    expect(r.path).not.toContain("L");
    expect(r.path).not.toContain("NaN");
    expect(r.last).not.toBeNull();
    expect(r.last!.x).toBeGreaterThanOrEqual(DEFAULT_VIEW.x);
    expect(r.last!.x).toBeLessThanOrEqual(DEFAULT_VIEW.x + DEFAULT_VIEW.w);
    expect(r.last!.y).toBeGreaterThanOrEqual(DEFAULT_VIEW.y);
    expect(r.last!.y).toBeLessThanOrEqual(DEFAULT_VIEW.y + DEFAULT_VIEW.h);
  });

  // ── all-same-lat (zero lat span) ─────────────────────────────────────────
  // lat span = 0 → y maps to vertical center of inner box (~50 for 100×100 view)
  it("all-same-lat → no NaN, all y at vertical center (~50 for 100×100 view)", () => {
    const view = { x: 0, y: 0, w: 100, h: 100 };
    const r = projectTrack([40, 40, 40], [-74, -73, -72], view);
    expect(r.path).not.toContain("NaN");
    const ys = [...r.path.matchAll(/[ML]-?[\d.]+ (-?[\d.]+)/g)].map((m) =>
      parseFloat(m[1]),
    );
    expect(ys).toHaveLength(3);
    ys.forEach((y) => expect(y).toBeCloseTo(50, 0));
  });

  // ── all-same-lon (zero lon span) ─────────────────────────────────────────
  // lon span = 0 → x maps to horizontal center of inner box (~50 for 100×100 view)
  it("all-same-lon → no NaN, all x at horizontal center (~50 for 100×100 view)", () => {
    const view = { x: 0, y: 0, w: 100, h: 100 };
    const r = projectTrack([40, 41, 42], [-74, -74, -74], view);
    expect(r.path).not.toContain("NaN");
    const xs = [...r.path.matchAll(/[ML](\S+) /g)].map((m) =>
      parseFloat(m[1]),
    );
    expect(xs).toHaveLength(3);
    xs.forEach((x) => expect(x).toBeCloseTo(50, 0));
  });
});
