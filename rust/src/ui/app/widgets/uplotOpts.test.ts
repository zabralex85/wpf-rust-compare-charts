import { describe, it, expect } from "vitest";
import { lineOpts } from "./uplotOpts";

describe("lineOpts", () => {
  it("builds a dark time-series config", () => {
    const o = lineOpts(300, 120);
    expect(o.width).toBe(300);
    expect(o.height).toBe(120);
    expect(o.scales?.x?.time).toBe(true);
    expect(o.scales?.y?.auto).toBe(true);
    // 2 series: x + the value line
    expect(o.series.length).toBe(2);
    expect(o.series[1].stroke).toBe("#38c5e0");
    expect(o.legend?.show).toBe(false);
  });
});
