import { describe, it, expect } from "vitest";
import { fmtCoord } from "./geoFormat";

describe("fmtCoord", () => {
  it("formats the last point with hemisphere letters", () => {
    expect(fmtCoord([32.0853, 32.1], [34.7818, 34.9])).toBe("32.1000°N 34.9000°E");
  });
  it("uses S/W for negative lat/lon", () => {
    expect(fmtCoord([-12.5], [-77.0])).toBe("12.5000°S 77.0000°W");
  });
  it("empty → em dash", () => {
    expect(fmtCoord([], [])).toBe("—");
  });
});
