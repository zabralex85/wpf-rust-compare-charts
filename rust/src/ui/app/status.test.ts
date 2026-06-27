import { describe, it, expect } from "vitest";
import { deriveStatus } from "./status";
import { TelemetryStore } from "../../data/store";
import type { MetaMessage } from "../../types";

function meta(): MetaMessage {
  return {
    type: "meta", rate_hz: 10,
    enum_values: [
      { channel_id: 1, code: 0, label: "Normal", severity: "ok" },
      { channel_id: 1, code: 1, label: "Critical", severity: "critical" },
      { channel_id: 2, code: 0, label: "OK", severity: "ok" },
      { channel_id: 2, code: 1, label: "Warn", severity: "caution" },
    ],
    channels: [
      { id: 1, name: "M1", column_name: "m1", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 1, addr: "I_01" },
      { id: 2, name: "M2", column_name: "m2", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 2, addr: "I_01" },
    ],
  };
}

describe("deriveStatus", () => {
  it("counts critical=alarm, caution, and link", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [1, 1] });
    const st = deriveStatus(s);
    expect(st.alarms).toBe(1);   // m1 -> critical
    expect(st.cautions).toBe(1); // m2 -> caution
    expect(st.linkOk).toBe(true);
  });

  it("reports no link before meta", () => {
    const st = deriveStatus(new TelemetryStore());
    expect(st.linkOk).toBe(false);
    expect(st.alarms).toBe(0);
  });
});
