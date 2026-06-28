import { describe, it, expect } from "vitest";
import { trackToGeoJSON } from "./trackGeo";

describe("trackToGeoJSON", () => {
  it("builds a lon,lat LineString", () => {
    const f = trackToGeoJSON([32.0, 32.1], [34.8, 34.9]);
    expect(f.geometry.type).toBe("LineString");
    expect(f.geometry.coordinates).toEqual([[34.8, 32.0], [34.9, 32.1]]);
  });
  it("empty input → empty coords", () => {
    expect(trackToGeoJSON([], []).geometry.coordinates).toEqual([]);
  });
});
