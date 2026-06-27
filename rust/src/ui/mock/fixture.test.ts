import { describe, it, expect } from "vitest";
import { MOCK_META, MOCK_FRAMES, applyMockSnapshot } from "./fixture";
import { TelemetryStore } from "../../data/store";

describe("mock fixture", () => {
  it("has 30 channels, rate, duration, and an inu_mode2 enum", () => {
    expect(MOCK_META.channels.length).toBe(30);
    expect(MOCK_META.rate_hz).toBe(10);
    expect(MOCK_META.duration_s).toBeGreaterThan(0);
    expect(MOCK_META.enum_values.some((e) => e.severity === "critical")).toBe(true);
    expect(MOCK_FRAMES.length).toBeGreaterThan(0);
  });

  it("applies to a store deterministically", () => {
    const s = new TelemetryStore();
    applyMockSnapshot(s);
    expect(s.channels().length).toBe(30);
    expect(s.lastTsMs()).toBe(MOCK_FRAMES[MOCK_FRAMES.length - 1].ts_ms);
    const roll = s.channels().find((c) => c.column_name === "roll")!;
    expect(s.latest(roll.id)).toBeDefined();
  });
});
