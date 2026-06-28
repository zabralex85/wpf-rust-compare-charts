// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { ParamPanel } from "./ParamPanel";
import { TelemetryStore } from "../../data/store";

afterEach(() => cleanup());

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

describe("ParamPanel", () => {
  it("renders group headers, rows, value, and the CH count", () => {
    const { container } = render(<ParamPanel store={store()} />);
    const t = container.textContent ?? "";
    expect(t).toContain("PARAMETERS");
    expect(t).toContain("2 CH");
    expect(t).toContain("Attitude");
    expect(t).toContain("INU Mode");
    expect(t).toContain("Roll");
    expect(t).toContain("90.000");
    expect(t).toContain("Critical");
  });

  it("marks the critical row", () => {
    const { container } = render(<ParamPanel store={store()} />);
    expect(container.querySelector(".param-row-crit")).not.toBeNull();
  });

  it("param rows are draggable and emit a DragPayload on dragstart", () => {
    render(<ParamPanel store={store()} />);
    const row = document.querySelector('[data-prow="1"]') as HTMLElement;
    expect(row).toBeTruthy();
    expect(row.getAttribute("draggable")).toBe("true");
    let captured = "";
    const dt = { effectAllowed: "", setData: (_t: string, v: string) => { captured = v; } };
    row.dispatchEvent(Object.assign(new Event("dragstart", { bubbles: true }), { dataTransfer: dt }));
    expect(JSON.parse(captured)).toMatchObject({ channelId: 1, name: expect.any(String), unit: "deg" });
  });
});
