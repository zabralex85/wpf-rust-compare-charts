import { describe, it, expect } from "vitest";
import { mapStyle } from "./mapStyle";

describe("mapStyle", () => {
  it("renders geometry only (no label/glyphs)", () => {
    const s = mapStyle("http://127.0.0.1:9002");
    expect(s.sources.basemap).toMatchObject({ type: "vector", url: "http://127.0.0.1:9002/tiles.json" });
    expect("glyphs" in s).toBe(false);
    expect(s.layers.some((l) => l.type === "symbol")).toBe(false);
    expect(s.layers.some((l) => l.id === "water")).toBe(true);
    expect(s.layers.find((l) => l.type === "background")).toBeTruthy();
  });
});
