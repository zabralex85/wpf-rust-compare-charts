import { describe, it, expect } from "vitest";
import { FpsMeter, latencyMs } from "./fps";

describe("FpsMeter", () => {
  it("returns 0 before two ticks", () => {
    const m = new FpsMeter();
    expect(m.fps()).toBe(0);
    m.tick(0);
    expect(m.fps()).toBe(0);
  });

  it("computes ~60fps from 16.67ms intervals", () => {
    const m = new FpsMeter();
    for (let i = 0; i <= 10; i++) m.tick(i * (1000 / 60));
    expect(Math.round(m.fps())).toBe(60);
    expect(m.frameTimeMs()).toBeCloseTo(1000 / 60, 1);
  });

  it("respects the window size (keeps only recent frames)", () => {
    const m = new FpsMeter(3);
    for (let i = 0; i < 10; i++) m.tick(i * 10);
    // only last 3 timestamps retained -> 2 intervals of 10ms -> 100fps
    expect(Math.round(m.fps())).toBe(100);
  });
});

describe("latencyMs", () => {
  it("is now minus emit", () => {
    expect(latencyMs(1000, 1075)).toBe(75);
  });
});
