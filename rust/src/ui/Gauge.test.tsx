// @vitest-environment jsdom
import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { Gauge } from "./Gauge";

describe("Gauge", () => {
  it("rotates the needle to the mid angle at mid value", () => {
    const { getByTestId } = render(<Gauge label="Roll" unit="deg" value={0} min={-180} max={180} />);
    const needle = getByTestId("needle");
    expect(needle.getAttribute("transform")).toContain("rotate(0");
  });

  it("rotates to the start angle at min value", () => {
    const { getByTestId } = render(<Gauge label="Roll" unit="deg" value={-180} min={-180} max={180} />);
    expect(getByTestId("needle").getAttribute("transform")).toContain("rotate(-120");
  });
});
