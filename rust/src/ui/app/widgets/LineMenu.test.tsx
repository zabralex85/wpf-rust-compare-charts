// @vitest-environment jsdom
import { it, expect, afterEach, vi } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
import { LineMenu } from "./LineMenu";

afterEach(cleanup);

it("renders three actions and fires callbacks", () => {
  const onZoomIn = vi.fn(), onZoomOut = vi.fn(), onReset = vi.fn();
  render(<LineMenu x={10} y={20} onZoomIn={onZoomIn} onZoomOut={onZoomOut} onReset={onReset} />);
  expect(screen.getByTestId("line-menu")).toBeTruthy();
  fireEvent.click(screen.getByText("Zoom in")); expect(onZoomIn).toHaveBeenCalled();
  fireEvent.click(screen.getByText("Zoom out")); expect(onZoomOut).toHaveBeenCalled();
  fireEvent.click(screen.getByText(/Reset/)); expect(onReset).toHaveBeenCalled();
});
