// @vitest-environment jsdom
import { describe, it, expect, afterEach, vi } from "vitest";
import { render, cleanup, fireEvent } from "@testing-library/react";
import { TransportBar } from "./TransportBar";

afterEach(() => cleanup());

describe("TransportBar", () => {
  it("renders clock, samples count, and elapsed", () => {
    const { container } = render(
      <TransportBar clock={{ hms: "11:33:26", ms: "878" }} rideTag="33:26.730"
        rateHz={10} samples={7451} elapsedMs={(2 * 3600 + 4 * 60 + 11) * 1000} scrubberFrac={0.88}
        paused={false} onPlayPause={() => {}} onSeek={() => {}} />,
    );
    const t = container.textContent ?? "";
    expect(t).toContain("11:33:26");
    expect(t).toContain("7,451");
    expect(t).toContain("2:04:11");
  });

  it("places the scrubber marker at the given fraction", () => {
    const { container } = render(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0.5}
        paused={false} onPlayPause={() => {}} onSeek={() => {}} />,
    );
    const marker = container.querySelector('[data-testid="scrub-marker"]') as HTMLElement;
    expect(marker.style.left).toBe("50%");
  });

  it("calls onPlayPause when the play-pause button is clicked", () => {
    const onPlayPause = vi.fn();
    const { getByTestId } = render(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0}
        paused={false} onPlayPause={onPlayPause} onSeek={() => {}} />,
    );
    fireEvent.click(getByTestId("play-pause"));
    expect(onPlayPause).toHaveBeenCalledTimes(1);
  });

  it("shows ▶ when paused and ⏸ when playing", () => {
    const { getByTestId, rerender } = render(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0}
        paused={true} onPlayPause={() => {}} onSeek={() => {}} />,
    );
    expect(getByTestId("play-pause").textContent).toBe("▶");

    rerender(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0}
        paused={false} onPlayPause={() => {}} onSeek={() => {}} />,
    );
    expect(getByTestId("play-pause").textContent).toBe("⏸");
  });

  it("calls onSeek with the correct fraction when the scrub track is clicked", () => {
    const onSeek = vi.fn();
    const { getByTestId } = render(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0}
        paused={false} onPlayPause={() => {}} onSeek={onSeek} />,
    );
    const track = getByTestId("scrub-track");
    vi.spyOn(track, "getBoundingClientRect").mockReturnValue({
      left: 0, width: 200, right: 200, top: 0, bottom: 0, height: 0, x: 0, y: 0,
      toJSON: () => {},
    } as DOMRect);
    fireEvent.click(track, { clientX: 100 });
    expect(onSeek).toHaveBeenCalledTimes(1);
    expect(onSeek.mock.calls[0][0]).toBeCloseTo(0.5);
  });
});
