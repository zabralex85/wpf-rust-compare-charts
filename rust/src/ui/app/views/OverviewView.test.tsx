// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { OverviewView } from "./OverviewView";
import { TelemetryStore } from "../../../data/store";

afterEach(() => cleanup());

describe("OverviewView", () => {
  it("renders the param panel + a dashboard placeholder", () => {
    const s = new TelemetryStore();
    s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60, enum_values: [],
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" }] });
    const { container } = render(<OverviewView store={s} />);
    expect(container.querySelector('[data-testid="view-overview"]')).not.toBeNull();
    expect(container.textContent).toContain("PARAMETERS");
    expect(container.querySelector('[data-testid="overview-dash"]')).not.toBeNull();
  });

  it("shows waiting placeholder and no widgets when no meta has arrived", () => {
    const s = new TelemetryStore();
    const { container } = render(<OverviewView store={s} />);
    const dash = container.querySelector('[data-testid="overview-dash"]');
    expect(dash).not.toBeNull();
    expect(dash!.textContent).toContain("Waiting for telemetry");
    expect(container.querySelector('[data-testid="gauge"]')).toBeNull();
    expect(container.querySelector('[data-testid="linechart"]')).toBeNull();
    expect(container.querySelector('[data-testid="mapwidget"]')).toBeNull();
  });
});
