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

  it("uses the mock snapshot when ?mock=1 (no socket factory needed)", () => {
    // jsdom default location has no query; set it
    window.history.pushState({}, "", "/?mock=1");
    try {
      const { result } = renderHook(() => useTelemetry("ws://127.0.0.1:9999"));
      expect(result.current.store.channels().length).toBe(30);
    } finally {
      window.history.pushState({}, "", "/");
    }
  });

  it("exposes a send function on the snapshot", () => {
    window.history.pushState({}, "", "/?mock=1");
    try {
      const { result } = renderHook(() => useTelemetry("ws://127.0.0.1:9999"));
      expect(typeof result.current.send).toBe("function");
    } finally {
      window.history.pushState({}, "", "/");
    }
  });

  it("send is a no-op in mock mode and does not throw", () => {
    window.history.pushState({}, "", "/?mock=1");
    try {
      const { result } = renderHook(() => useTelemetry("ws://127.0.0.1:9999"));
      expect(() =>
        result.current.send('{"type":"cmd","action":"pause"}')
      ).not.toThrow();
    } finally {
      window.history.pushState({}, "", "/");
    }
  });
});
