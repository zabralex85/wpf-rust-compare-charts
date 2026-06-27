// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { Gauge } from "./Gauge";
import { gaugeViz } from "./gaugeViz";

afterEach(cleanup);

describe("Gauge component", () => {
  it("renders an SVG with data-testid gauge", () => {
    const { container } = render(
      <Gauge name="Roll" value={0} unit="deg" scalesOn={true} />
    );
    const gaugeElement = container.querySelector('[data-testid="gauge"]');
    expect(gaugeElement).not.toBeNull();
  });

  it("needle line x2 matches gaugeViz output", () => {
    const value = 0;
    const viz = gaugeViz(value);
    const { container } = render(
      <Gauge name="Roll" value={value} unit="deg" scalesOn={true} />
    );
    const needleLine = container.querySelector(".gauge-needle");
    expect(needleLine).not.toBeNull();
    if (needleLine) {
      const x2 = needleLine.getAttribute("x2");
      // Should be approximately 40 for value=0
      expect(x2).toBe(String(viz.nx));
      const y2 = needleLine.getAttribute("y2");
      expect(parseFloat(y2 || "")).toBeCloseTo(viz.ny);
    }
  });

  it("displays value text and name", () => {
    const { container } = render(
      <Gauge name="Roll" value={0} unit="deg" scalesOn={true} />
    );
    const textContent = container.textContent;
    expect(textContent).toContain("Roll");
    expect(textContent).toContain("deg");
  });

  it("shows scale labels when scalesOn is true", () => {
    const { container } = render(
      <Gauge name="Roll" value={0} unit="deg" scalesOn={true} />
    );
    const scaleLabels = container.querySelectorAll(".gauge-scale-label");
    expect(scaleLabels.length).toBeGreaterThan(0);
  });

  it("hides scale labels when scalesOn is false", () => {
    const { container } = render(
      <Gauge name="Roll" value={0} unit="deg" scalesOn={false} />
    );
    const scaleLabels = container.querySelectorAll(".gauge-scale-label");
    expect(scaleLabels.length).toBe(0);
  });
});
