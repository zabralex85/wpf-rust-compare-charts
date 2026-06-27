// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, cleanup, fireEvent } from "@testing-library/react";
import { TopBar } from "./TopBar";

afterEach(() => cleanup());

const base = {
  clock: { hms: "11:33:26", ms: "878" },
  status: { alarms: 2, cautions: 1, linkOk: true },
  screen: "overview" as const,
  onScreen: vi.fn(),
  scalesOn: true,
  onToggleScales: vi.fn(),
};

describe("TopBar", () => {
  it("renders brand, clock, and alarm/caution counts", () => {
    const { container } = render(<TopBar {...base} />);
    const t = container.textContent ?? "";
    expect(t).toContain("INU");
    expect(t).toContain("11:33:26");
    expect(t).toContain("2 ALARM");
    expect(t).toContain("1 CAUTION");
  });

  it("fires onScreen when a tab is clicked", () => {
    const onScreen = vi.fn();
    const { getByText } = render(<TopBar {...base} onScreen={onScreen} />);
    fireEvent.click(getByText("FLIGHT TRACK"));
    expect(onScreen).toHaveBeenCalledWith("track");
  });
});
