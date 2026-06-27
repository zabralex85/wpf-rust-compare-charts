// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { Hud } from "./Hud";
import { TelemetryStore } from "../data/store";
import type { MetaMessage } from "../types";

function store(): TelemetryStore {
  const s = new TelemetryStore();
  const meta: MetaMessage = { type: "meta", rate_hz: 10, duration_s: 60, enum_values: [], channels: [
    { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" }] };
  s.applyMeta(meta);
  s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1000, values: [1] });
  s.applyMetrics({ type: "metrics", cpu_pct: 12.5, ram_mb: 80 });
  return s;
}

describe("Hud", () => {
  it("shows fps, latency, cpu and ram", () => {
    const { container } = render(<Hud store={store()} fps={60} frameTimeMs={16.7} nowMs={1075} />);
    const t = container.textContent ?? "";
    expect(t).toContain("60");     // fps
    expect(t).toContain("75");     // latency = 1075 - 1000
    expect(t).toContain("12.5");   // cpu
    expect(t).toContain("80");     // ram
  });
});
