// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup, act } from "@testing-library/react";
import type React from "react";
import { useWidgets } from "./useWidgets";
import type { ChannelMeta } from "../../../types";

afterEach(cleanup);

const channels: ChannelMeta[] = [
  { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I01" },
  { id: 2, name: "SkyPitch", column_name: "sky_pitch", unit: "g", type: "real", min: -2, max: 2, widget: "gauge", display_order: 2, addr: "I02" },
];

function Harness(): React.JSX.Element {
  const w = useWidgets(channels);
  return (
    <div>
      <div data-testid="count">{w.widgets.length}</div>
      <button data-testid="add" onClick={() => w.add({ channelId: 1, name: "Roll", unit: "deg" }, 2, 2)}>add</button>
      <button data-testid="rm" onClick={() => w.remove(w.widgets[w.widgets.length - 1].id)}>rm</button>
    </div>
  );
}

describe("useWidgets", () => {
  it("seeds from defaultWidgets and supports add/remove with minted ids", () => {
    render(<Harness />);
    const start = Number(screen.getByTestId("count").textContent);
    expect(start).toBeGreaterThan(0); // gauge for ch2 at least
    act(() => { screen.getByTestId("add").click(); });
    expect(Number(screen.getByTestId("count").textContent)).toBe(start + 1);
    act(() => { screen.getByTestId("rm").click(); });
    expect(Number(screen.getByTestId("count").textContent)).toBe(start);
  });
});
