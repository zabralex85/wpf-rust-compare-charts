import { describe, it, expect } from "vitest";
import { rideClock, rideProgress } from "./ridetime";

describe("rideClock", () => {
  it("formats ride ms as HH:MM:SS + millis", () => {
    const c = rideClock(11 * 3600_000 + 33 * 60_000 + 26_000 + 878);
    expect(c.hms).toBe("11:33:26");
    expect(c.ms).toBe("878");
  });
  it("allows >24h", () => {
    expect(rideClock(30 * 3600_000).hms).toBe("30:00:00");
  });
});

describe("rideProgress", () => {
  it("is ts over duration, clamped", () => {
    expect(rideProgress(300_000, 600_000)).toBeCloseTo(0.5);
    expect(rideProgress(900_000, 600_000)).toBe(1);
    expect(rideProgress(100, 0)).toBe(0);
  });
});
