// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { WidgetGrid } from "./WidgetGrid";
import { TelemetryStore } from "../../../data/store";
import { applyMockSnapshot } from "../../mock/fixture";

afterEach(cleanup);

function makeStore(): TelemetryStore {
  const store = new TelemetryStore();
  applyMockSnapshot(store);
  return store;
}

describe("WidgetGrid", () => {
  it("renders at least one gauge", () => {
    const store = makeStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={true} />);
    const gauges = container.querySelectorAll('[data-testid="gauge"]');
    expect(gauges.length).toBeGreaterThanOrEqual(1);
  });

  it("renders at least one linechart", () => {
    const store = makeStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={true} />);
    const charts = container.querySelectorAll('[data-testid="linechart"]');
    expect(charts.length).toBeGreaterThanOrEqual(1);
  });

  it("renders exactly one mapwidget", () => {
    const store = makeStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={true} />);
    const maps = container.querySelectorAll('[data-testid="mapwidget"]');
    expect(maps.length).toBe(1);
  });

  it("each widget cell shows a name header", () => {
    const store = makeStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={true} />);
    const headers = container.querySelectorAll(".widget-cell-header");
    expect(headers.length).toBeGreaterThan(0);
  });

  it("widget cells carry gridColumn and gridRow span styles", () => {
    const store = makeStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={false} />);
    const cells = container.querySelectorAll(".widget-cell");
    expect(cells.length).toBeGreaterThan(0);
    const first = cells[0] as HTMLElement;
    expect(first.style.gridColumn).toContain("span");
    expect(first.style.gridRow).toContain("span");
  });

  it("renders an empty grid when the store has no channels", () => {
    const store = new TelemetryStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={true} />);
    expect(container.querySelector(".widgetgrid")).not.toBeNull();
    expect(container.querySelectorAll(".widget-cell").length).toBe(0);
  });

  it("scalesOn=false suppresses gauge tick marks", () => {
    const store = makeStore();
    const { container } = render(<WidgetGrid store={store} scalesOn={false} />);
    // When scalesOn is false Gauge renders no .gauge-ticks group
    expect(container.querySelectorAll(".gauge-ticks").length).toBe(0);
  });
});
