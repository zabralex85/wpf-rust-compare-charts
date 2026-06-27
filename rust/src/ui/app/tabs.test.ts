import { describe, it, expect } from "vitest";
import { SCREENS, formatCount, formatElapsed } from "./tabs";

describe("tabs model", () => {
  it("lists the three screens in order", () => {
    expect(SCREENS.map((s) => s.key)).toEqual(["overview", "track", "events"]);
    expect(SCREENS[1].label).toBe("FLIGHT TRACK");
  });
});

describe("formatters", () => {
  it("formats counts with thousands separators", () => {
    expect(formatCount(7451)).toBe("7,451");
    expect(formatCount(0)).toBe("0");
  });
  it("formats elapsed as H:MM:SS", () => {
    expect(formatElapsed((2 * 3600 + 4 * 60 + 11) * 1000)).toBe("2:04:11");
  });
});
