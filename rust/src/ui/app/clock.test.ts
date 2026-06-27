import { describe, it, expect } from "vitest";
import { formatClock, formatRideTag } from "./clock";

describe("formatClock", () => {
  it("formats unix ms as HH:MM:SS + millis (UTC)", () => {
    // 1970-01-01 11:33:26.878 UTC = 41606878 ms
    const c = formatClock(41_606_878);
    expect(c.hms).toBe("11:33:26");
    expect(c.ms).toBe("878");
  });
});

describe("formatRideTag", () => {
  it("formats ms-since-start as MM:SS.mmm", () => {
    expect(formatRideTag(33 * 60_000 + 26_000 + 730)).toBe("33:26.730");
  });
});
