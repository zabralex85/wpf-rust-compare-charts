// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, cleanup, fireEvent } from "@testing-library/react";
import { AppShell } from "./AppShell";

beforeEach(() => {
  let id = 0;
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => { id++; setTimeout(() => cb(id), 0); return id; });
  vi.stubGlobal("cancelAnimationFrame", () => {});
});
afterEach(() => cleanup());

describe("AppShell", () => {
  it("shows the overview view by default and switches tabs", () => {
    const { container, getByText } = render(<AppShell />);
    expect(container.querySelector('[data-testid="view-overview"]')).not.toBeNull();
    fireEvent.click(getByText("EVENTS"));
    expect(container.querySelector('[data-testid="view-events"]')).not.toBeNull();
    expect(container.querySelector('[data-testid="view-overview"]')).toBeNull();
  });
});
