import { describe, it, expect } from "vitest";
import { gaugeViz, fmtScale, fmtNum } from "./gaugeViz";

describe("gaugeViz", () => {
  it("centers the needle at value 0 (angle -? ... mid)", () => {
    const g = gaugeViz(0);
    // v=0 -> frac=0.5 -> ang = -135 + 0.5*270 = 0 deg -> needle straight up
    expect(g.angleDeg).toBeCloseTo(0, 5);
    expect(g.nx).toBeCloseTo(40, 3);
    expect(g.ny).toBeCloseTo(12, 3); // 40 - 28*cos(0)
  });
  it("computes a nice round scale and value text", () => {
    const g = gaugeViz(19.648);
    expect(g.valueText).toBe("19.648");
    expect(g.gMax).toBe("50"); // raw=25.54... -> ex=1 ff=2.55 -> nf=5 -> R=50 -> fmtScale(50)="50"
  });
  it("fmtScale strips trailing zeros", () => {
    expect(fmtScale(50)).toBe("50");
    expect(fmtScale(2.5)).toBe("2.5");
    expect(fmtScale(0)).toBe("0");
  });
  it("does not mangle integer scale labels >= 100", () => {
    expect(fmtScale(100)).toBe("100");
    expect(fmtScale(200)).toBe("200");
    expect(fmtScale(500)).toBe("500");
  });
  it("gaugeViz handles large values (scale >= 100)", () => {
    // value 300 -> raw 390 -> ex 2 ff 3.9 nf 5 -> R 500
    expect(gaugeViz(300).gMax).toBe("500");
  });
  it("fmtNum formats numbers with appropriate precision", () => {
    expect(fmtNum(123.456)).toBe("123.5"); // a >= 100 → toFixed(1)
    expect(fmtNum(12.3456789)).toBe("12.346"); // a >= 1 → toFixed(3)
    expect(fmtNum(0.0001234)).toBe("0.000123"); // a < 1 → toFixed(6)
    expect(fmtNum(Infinity)).toBe("—"); // non-finite
  });
});
