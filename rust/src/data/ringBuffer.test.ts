import { describe, it, expect } from "vitest";
import { ChannelSeries } from "./ringBuffer";

describe("ChannelSeries", () => {
  it("keeps samples within the window and evicts older ones", () => {
    const s = new ChannelSeries(1000); // 1s window
    s.push(0, 10);
    s.push(500, 11);
    s.push(1000, 12);
    s.push(1600, 13); // window now [600,1600] -> drops ts=0 and ts=500
    const { xs, ys } = s.arrays();
    expect(xs).toEqual([1000, 1600]);
    expect(ys).toEqual([12, 13]);
  });

  it("arrays() returns parallel xs/ys of equal length", () => {
    const s = new ChannelSeries(10_000);
    s.push(0, 1);
    s.push(100, 2);
    const { xs, ys } = s.arrays();
    expect(xs.length).toBe(ys.length);
    expect(s.len()).toBe(2);
  });
});
