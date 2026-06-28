import { describe, it, expect } from "vitest";
import { mapStyle } from "./mapStyle";

describe("mapStyle", () => {
  it("points sources + glyphs at the local base and has layers", () => {
    const s = mapStyle("http://127.0.0.1:9002");
    expect(s.sources.basemap).toMatchObject({ type: "vector", url: "http://127.0.0.1:9002/tiles.json" });
    expect(s.glyphs).toBe("http://127.0.0.1:9002/glyphs/{fontstack}/{range}.pbf");
    expect(Array.isArray(s.layers) && s.layers.length).toBeGreaterThan(0);
    // a background layer in the INU bg
    const bg = s.layers.find((l) => l.type === "background");
    expect(bg).toBeTruthy();
  });
});
