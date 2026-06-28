import { describe, it, expect } from "vitest";
import { mapStyle } from "./mapStyle";

describe("mapStyle", () => {
  it("wires source + glyphs to the local base and includes geometry + label layers", () => {
    const s = mapStyle("http://127.0.0.1:9002");
    expect(s.sources.basemap).toMatchObject({ type: "vector", url: "http://127.0.0.1:9002/tiles.json" });
    expect(s.glyphs).toBe("http://127.0.0.1:9002/glyphs/{fontstack}/{range}.pbf");
    expect(s.layers.some((l) => l.id === "water")).toBe(true);
    expect(s.layers.find((l) => l.type === "background")).toBeTruthy();
    // place labels restored
    const label = s.layers.find((l) => l.id === "place-label");
    expect(label?.type).toBe("symbol");
  });
});
