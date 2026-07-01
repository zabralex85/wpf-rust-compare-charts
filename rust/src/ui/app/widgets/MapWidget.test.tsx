// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { MapWidget } from "./MapWidget";

afterEach(cleanup);

// The map renders the MapLibre basemap directly (1:1 with the .NET Skia basemap). There is no
// decorative SVG "grid view" and no OSM/GRID toggle. MapLibre itself is never constructed under
// jsdom (no WebGL), so these tests cover the container + chrome overlays, not the GL canvas.
describe("MapWidget component", () => {
  const lat = [32.0, 32.1, 32.2, 32.15, 32.05];
  const lon = [34.7, 34.75, 34.8, 34.9, 34.85];

  it("renders [data-testid='mapwidget']", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector('[data-testid="mapwidget"]')).not.toBeNull();
  });

  it("renders the MapLibre basemap container", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector(".mapwidget-osm")).not.toBeNull();
  });

  it("has no decorative grid view (no svg, rings, or track path)", () => {
    const { container } = render(<MapWidget lat={lat} lon={lon} />);
    expect(container.querySelector("svg")).toBeNull();
    expect(container.querySelector(".mapwidget-ring")).toBeNull();
    expect(container.querySelector(".mapwidget-track")).toBeNull();
  });

  it("has no OSM/GRID toggle button", () => {
    const { queryByText } = render(<MapWidget lat={lat} lon={lon} />);
    expect(queryByText("OSM MAP")).toBeNull();
    expect(queryByText("GRID VIEW")).toBeNull();
  });

  it("renders compass, coords readout, and scale bar", () => {
    const { getByText } = render(<MapWidget lat={[32.0853]} lon={[34.7818]} />);
    expect(getByText("N↑")).toBeTruthy();
    expect(getByText("32.0853°N 34.7818°E")).toBeTruthy();
    expect(getByText("2 km")).toBeTruthy();
  });

  it("renders without crashing when arrays are empty", () => {
    const { container } = render(<MapWidget lat={[]} lon={[]} />);
    expect(container.querySelector(".mapwidget-osm")).not.toBeNull();
  });
});
