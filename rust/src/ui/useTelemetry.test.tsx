// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useTelemetry } from "./useTelemetry";

// stub rAF so the loop runs deterministically
beforeEach(() => {
  let id = 0;
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => { id++; setTimeout(() => cb(id), 0); return id; });
  vi.stubGlobal("cancelAnimationFrame", () => {});
});

describe("useTelemetry", () => {
  it("returns a store and a status", () => {
    const { result } = renderHook(() => useTelemetry("ws://127.0.0.1:9999"));
    expect(result.current.store).toBeDefined();
    expect(["connecting", "open", "closed"]).toContain(result.current.status);
  });
});
