// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { MapWidget } from "./MapWidget";

afterEach(cleanup);

describe("MapWidget component", () => {
  const lat = [32.0, 32.1, 32.2, 32.15, 32.05];
  const lon = [34.7, 34.75, 34.8, 34.9, 34.85];

  it("renders [data-testid='mapwidget']", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector('[data-testid="mapwidget"]')).not.toBeNull();
  });

  it("contains an svg element", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector("svg")).not.toBeNull();
  });

  it("contains at least one path element", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelectorAll("path").length).toBeGreaterThan(0);
  });

  it("track path d attribute starts with M for non-empty data", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    const track = container.querySelector(".mapwidget-track");
    expect(track?.getAttribute("d")?.[0]).toBe("M");
  });

  it("marker element renders when data is present", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector(".mapwidget-marker")).not.toBeNull();
  });

  it("renders svg without crashing when arrays are empty", () => {
    const { container } = render(<MapWidget lat={[]} lon={[]} />);
    expect(container.querySelector("svg")).not.toBeNull();
  });

  it("no marker when arrays are empty", () => {
    const { container } = render(<MapWidget lat={[]} lon={[]} />);
    expect(container.querySelector(".mapwidget-marker")).toBeNull();
  });

  it("grid pattern is present in defs", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector("defs pattern")).not.toBeNull();
  });

  it("range rings are rendered (at least one circle besides marker)", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    const circles = container.querySelectorAll("circle");
    // at minimum the 3 range rings + 1 marker circle = 4
    expect(circles.length).toBeGreaterThanOrEqual(3);
  });

  it("renders compass, coords readout, and scale bar", () => {
    const { getByText } = render(<MapWidget lat={[32.0853]} lon={[34.7818]} />);
    expect(getByText("N↑")).toBeTruthy();
    expect(getByText("32.0853°N 34.7818°E")).toBeTruthy();
    expect(getByText("2 km")).toBeTruthy();
  });
});
