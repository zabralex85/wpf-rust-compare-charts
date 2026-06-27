// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup, fireEvent, screen } from "@testing-library/react";
import { LineChart } from "./LineChart";

afterEach(cleanup);

describe("LineChart component", () => {
  const xs = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
  const ys = [10, 20, 15, 25, 18, 22, 13, 28, 17, 21];

  it("renders [data-testid='linechart']", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={true} />
    );
    expect(container.querySelector('[data-testid="linechart"]')).not.toBeNull();
  });

  it("path d attribute starts with M for non-empty data", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={true} />
    );
    const path = container.querySelector(".linechart-path");
    expect(path?.getAttribute("d")?.[0]).toBe("M");
  });

  it("displays value text and unit", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={true} />
    );
    expect(container.textContent).toContain("21");
    expect(container.textContent).toContain("m/s");
  });

  it("displays name label", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={true} />
    );
    expect(container.textContent).toContain("Speed");
  });

  it("axis scale labels absent when scalesOn is false", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={false} />
    );
    const scaleLabels = container.querySelectorAll(".linechart-scale-label");
    expect(scaleLabels.length).toBe(0);
  });

  it("shows exactly 5 y-axis scale labels when scalesOn is true", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={true} />
    );
    const yLabels = container.querySelectorAll(
      ".linechart-y-labels .linechart-scale-label"
    );
    expect(yLabels.length).toBe(5);
  });

  it("value overlay uses fmtNum of value prop", () => {
    const { container } = render(
      <LineChart name="Speed" xs={xs} ys={ys} unit="m/s" value={21} scalesOn={true} />
    );
    // fmtNum(21) = "21.000" (a>=1 → toFixed(3))
    expect(container.textContent).toContain("21.000");
  });

  it("shows a hover tooltip on mouse move and a zoom badge when zoom>1", () => {
    render(
      <LineChart
        name="Roll"
        xs={[0, 1, 2]}
        ys={[5, 7, 9]}
        unit="deg"
        value={9}
        scalesOn={true}
        zoom={2}
      />
    );
    // zoom badge renders ×2 as the complete text of the badge span
    expect(screen.getByText("×2")).toBeTruthy();

    const surface = document.querySelector(
      '[data-testid="linechart"] [data-hover-surface]'
    ) as HTMLElement;

    // clientX >= rect.right (0 in jsdom) → rel clamped to 1 → last sample (val=9)
    fireEvent.mouseMove(surface, { clientX: 9999, clientY: 0 });

    // tooltip element appears; check via className to avoid multiple-match from
    // getByText (value overlay + y-label yMid also contain "9.000")
    const tooltip = document.querySelector(".linechart-tooltip") as HTMLElement;
    expect(tooltip).not.toBeNull();
    expect(tooltip.textContent).toMatch(/9\.000/);

    fireEvent.mouseLeave(surface);
    // tooltip disappears after mouseLeave
    expect(document.querySelector(".linechart-tooltip")).toBeNull();
  });
});
