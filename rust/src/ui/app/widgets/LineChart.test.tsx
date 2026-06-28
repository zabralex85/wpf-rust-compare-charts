// @vitest-environment jsdom
// uPlot calls window.matchMedia at module-load time; mock the module so the
// import side-effect never fires in jsdom. vi.mock is hoisted above imports.
import { vi, describe, it, expect, afterEach } from "vitest";

vi.mock("uplot", () => ({
  default: class UPlotMock {
    constructor() {}
    setScale() {}
    setData() {}
    setSize() {}
    destroy() {}
  },
}));

import { render, cleanup } from "@testing-library/react";
import { LineChart } from "./LineChart";

afterEach(cleanup);

describe("LineChart component", () => {
  it("renders [data-testid='linechart']", () => {
    const { container } = render(
      <LineChart name="Roll" xs={[0, 1000, 2000]} ys={[1, 2, 3]} unit="deg" value={3} scalesOn={true} />
    );
    expect(container.querySelector('[data-testid="linechart"]')).not.toBeNull();
  });

  it("value overlay shows fmtNum(value)", () => {
    const { container } = render(
      <LineChart name="Roll" xs={[0, 1000, 2000]} ys={[1, 2, 3]} unit="deg" value={3} scalesOn={true} />
    );
    // fmtNum(3) = "3.000" (3 >= 1 → toFixed(3))
    expect(container.textContent).toContain("3.000");
  });

  it("renders the channel name", () => {
    const { container } = render(
      <LineChart name="Roll" xs={[0, 1000, 2000]} ys={[1, 2, 3]} unit="deg" value={3} scalesOn={true} />
    );
    expect(container.textContent).toContain("Roll");
  });

  it("does not crash in jsdom (uPlot guard active)", () => {
    // uPlot constructor is never reached — hasCanvas() returns false in jsdom
    expect(() => {
      render(
        <LineChart name="Roll" xs={[0, 1000, 2000]} ys={[1, 2, 3]} unit="deg" value={3} scalesOn={false} />
      );
    }).not.toThrow();
  });
});
