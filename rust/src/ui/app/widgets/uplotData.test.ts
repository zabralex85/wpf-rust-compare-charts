import { describe, it, expect } from "vitest";
import { toUPlotData, scrollWindow } from "./uplotData";

describe("toUPlotData", () => {
  it("converts ms→s, index-aligned", () => {
    expect(toUPlotData([0, 1000, 2000], [1, 2, 3])).toEqual([[0, 1, 2], [1, 2, 3]]);
  });
  it("min-length guards + empty", () => {
    expect(toUPlotData([0, 1000], [9])).toEqual([[0], [9]]);
    expect(toUPlotData([], [])).toEqual([[], []]);
  });
});
describe("scrollWindow", () => {
  it("is [last-window, last] in seconds", () => {
    expect(scrollWindow([0, 5000, 10000], 4000)).toEqual([6, 10]); // (10000-4000)/1000 .. 10000/1000
  });
  it("empty → [0, window s]", () => {
    expect(scrollWindow([], 60000)).toEqual([0, 60]);
  });
});
