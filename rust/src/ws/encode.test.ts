import { describe, it, expect } from "vitest";
import { encodeCmd } from "./encode";

describe("encodeCmd", () => {
  it("encodes pause without ts_ms", () => {
    const result = encodeCmd("pause");
    expect(result).toBe('{"type":"cmd","action":"pause"}');
  });

  it("encodes resume without ts_ms", () => {
    const result = encodeCmd("resume");
    expect(result).toBe('{"type":"cmd","action":"resume"}');
  });

  it("encodes seek with ts_ms", () => {
    const result = encodeCmd("seek", 4200);
    const parsed = JSON.parse(result);
    expect(parsed).toEqual({
      type: "cmd",
      action: "seek",
      ts_ms: 4200,
    });
  });

  it("does not include ts_ms when undefined for seek", () => {
    const result = encodeCmd("seek");
    const parsed = JSON.parse(result);
    expect(parsed).toEqual({
      type: "cmd",
      action: "seek",
    });
    expect("ts_ms" in parsed).toBe(false);
  });

  it("does not include ts_ms when undefined for pause", () => {
    const result = encodeCmd("pause");
    expect(JSON.parse(result)).not.toHaveProperty("ts_ms");
  });

  it("does not include ts_ms when undefined for resume", () => {
    const result = encodeCmd("resume");
    expect(JSON.parse(result)).not.toHaveProperty("ts_ms");
  });

  it("ignores ts_ms for pause", () => {
    const result = encodeCmd("pause", 1000);
    const parsed = JSON.parse(result);
    expect("ts_ms" in parsed).toBe(false);
  });

  it("ignores ts_ms for resume", () => {
    const result = encodeCmd("resume", 1000);
    const parsed = JSON.parse(result);
    expect("ts_ms" in parsed).toBe(false);
  });
});
