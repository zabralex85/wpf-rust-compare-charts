import { describe, it, expect } from "vitest";
import { gaugeAngle } from "./geometry";

describe("gaugeAngle", () => {
  it("maps min/mid/max across the sweep", () => {
    expect(gaugeAngle(-180, -180, 180, -120, 120)).toBe(-120);
    expect(gaugeAngle(0, -180, 180, -120, 120)).toBe(0);
    expect(gaugeAngle(180, -180, 180, -120, 120)).toBe(120);
  });

  it("clamps out-of-range values", () => {
    expect(gaugeAngle(999, 0, 10, 0, 90)).toBe(90);
    expect(gaugeAngle(-5, 0, 10, 0, 90)).toBe(0);
  });

  it("handles degenerate min==max", () => {
    expect(gaugeAngle(5, 5, 5, 0, 90)).toBe(0);
  });
});
