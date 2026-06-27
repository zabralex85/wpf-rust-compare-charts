import { describe, it, expect } from "vitest";
import { paramRow } from "./paramrow";
import { TelemetryStore } from "../../data/store";

function store(): TelemetryStore {
  const s = new TelemetryStore();
  s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60,
    enum_values: [{ channel_id: 2, code: 1, label: "Critical", severity: "critical" }],
    channels: [
      { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" },
      { id: 2, name: "Mode", column_name: "inu_mode2", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 2, addr: "I_01" },
    ] });
  s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [90, 1] });
  return s;
}

describe("paramRow", () => {
  it("formats a real channel value, not critical in-band", () => {
    const s = store();
    const r = paramRow(s.channels()[0], s);
    expect(r.text).toBe("90.000");
    expect(r.critical).toBe(false);
  });
  it("marks an enum critical channel", () => {
    const s = store();
    const r = paramRow(s.channels()[1], s);
    expect(r.text).toBe("Critical");
    expect(r.critical).toBe(true);
    expect(r.dotColor).toBe("#d22"); // severityColor("critical")
  });
  it("shows dash when no latest", () => {
    const s = new TelemetryStore();
    s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60, enum_values: [],
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" }] });
    expect(paramRow(s.channels()[0], s).text).toBe("—");
  });
});
