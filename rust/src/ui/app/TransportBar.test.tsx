// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { TransportBar } from "./TransportBar";

afterEach(() => cleanup());

describe("TransportBar", () => {
  it("renders clock, samples count, and elapsed", () => {
    const { container } = render(
      <TransportBar clock={{ hms: "11:33:26", ms: "878" }} rideTag="33:26.730"
        rateHz={10} samples={7451} elapsedMs={(2 * 3600 + 4 * 60 + 11) * 1000} scrubberFrac={0.88} />,
    );
    const t = container.textContent ?? "";
    expect(t).toContain("11:33:26");
    expect(t).toContain("7,451");
    expect(t).toContain("2:04:11");
  });

  it("places the scrubber marker at the given fraction", () => {
    const { container } = render(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0.5} />,
    );
    const marker = container.querySelector('[data-testid="scrub-marker"]') as HTMLElement;
    expect(marker.style.left).toBe("50%");
  });
});
