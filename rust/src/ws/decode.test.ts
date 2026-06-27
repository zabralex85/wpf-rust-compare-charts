import { describe, it, expect } from "vitest";
import { decodeMessage } from "./decode";

describe("decodeMessage", () => {
  it("decodes a meta message", () => {
    const json = JSON.stringify({
      type: "meta",
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" }],
      enum_values: [{ channel_id: 15, code: 1, label: "Critical", severity: "critical" }],
      rate_hz: 10,
      duration_s: 600,
    });
    const m = decodeMessage(json);
    expect(m.type).toBe("meta");
    if (m.type === "meta") {
      expect(m.channels[0].column_name).toBe("roll");
      expect(m.rate_hz).toBe(10);
    }
  });

  it("decodes a frame message", () => {
    const m = decodeMessage(JSON.stringify({ type: "frame", ts_ms: 100, emit_unix_ms: 1700000000000, values: [1.5, 2] }));
    expect(m.type).toBe("frame");
    if (m.type === "frame") expect(m.values).toEqual([1.5, 2]);
  });

  it("decodes a metrics message", () => {
    const m = decodeMessage(JSON.stringify({ type: "metrics", cpu_pct: 12.5, ram_mb: 80 }));
    expect(m.type).toBe("metrics");
    if (m.type === "metrics") expect(m.ram_mb).toBe(80);
  });

  it("throws on unknown type", () => {
    expect(() => decodeMessage(JSON.stringify({ type: "nope" }))).toThrow();
  });

  it("throws on invalid json", () => {
    expect(() => decodeMessage("{not json")).toThrow();
  });
});
