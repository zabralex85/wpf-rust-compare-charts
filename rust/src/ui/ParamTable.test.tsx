// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { ParamTable } from "./ParamTable";
import { TelemetryStore } from "../data/store";
import type { MetaMessage } from "../types";

function store(): TelemetryStore {
  const s = new TelemetryStore();
  const meta: MetaMessage = {
    type: "meta", rate_hz: 10,
    enum_values: [{ channel_id: 2, code: 1, label: "Critical", severity: "critical" }],
    channels: [
      { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" },
      { id: 2, name: "Mode", column_name: "mode", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 2, addr: "I_01" },
    ],
  };
  s.applyMeta(meta);
  s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [90, 1] });
  return s;
}

describe("ParamTable", () => {
  it("renders one row per channel with name + addr", () => {
    const { container } = render(<ParamTable store={store()} />);
    const rows = container.querySelectorAll("tbody tr");
    expect(rows.length).toBe(2);
    expect(container.textContent).toContain("Roll");
    expect(container.textContent).toContain("I_01");
  });

  it("shows the enum label for an enum channel", () => {
    const { container } = render(<ParamTable store={store()} />);
    expect(container.textContent).toContain("Critical");
  });
});
