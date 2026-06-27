import { describe, it, expect } from "vitest";
import { TelemetryStore } from "./store";
import type { MetaMessage, FrameMessage } from "../types";

function meta(): MetaMessage {
  return {
    type: "meta",
    rate_hz: 10,
    duration_s: 600,
    enum_values: [{ channel_id: 3, code: 1, label: "Critical", severity: "critical" }],
    channels: [
      { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" },
      { id: 2, name: "Lat", column_name: "lat", unit: "deg", type: "real", min: 31, max: 33, widget: "map_lat", display_order: 2, addr: "I_09" },
      { id: 3, name: "Lon", column_name: "lon", unit: "deg", type: "real", min: 34, max: 35, widget: "map_lon", display_order: 3, addr: "I_09" },
    ],
  };
}

describe("TelemetryStore", () => {
  it("creates series only for strip channels and records latest values", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    const f: FrameMessage = { type: "frame", ts_ms: 100, emit_unix_ms: 1700000000000, values: [12.5, 32.0, 34.5] };
    s.applyFrame(f);
    expect(s.latest(1)).toBe(12.5);
    expect(s.series(1)?.len()).toBe(1);   // strip channel buffered
    expect(s.series(2)).toBeUndefined();   // map channel has no strip series
    expect(s.lastEmitUnixMs()).toBe(1700000000000);
  });

  it("accumulates a GPS track from map_lat/map_lon channels", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [0, 32.0, 34.5] });
    s.applyFrame({ type: "frame", ts_ms: 100, emit_unix_ms: 2, values: [0, 32.1, 34.6] });
    expect(s.gpsTrack().lat).toEqual([32.0, 32.1]);
    expect(s.gpsTrack().lon).toEqual([34.5, 34.6]);
  });

  it("stores metrics", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyMetrics({ type: "metrics", cpu_pct: 5, ram_mb: 90 });
    expect(s.metrics()?.ram_mb).toBe(90);
  });

  it("clears latest values when meta is re-applied", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [12.5, 32.0, 34.5] });
    s.applyMeta(meta()); // reconnect resends meta
    expect(s.latest(1)).toBeUndefined();
    expect(s.gpsTrack().lat).toEqual([]);
  });

  it("keeps gps lat/lon arrays equal length", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [0, 32.0, 34.5] });
    s.applyFrame({ type: "frame", ts_ms: 100, emit_unix_ms: 2, values: [0, 32.1, 34.6] });
    const t = s.gpsTrack();
    expect(t.lat.length).toBe(t.lon.length);
  });

  it("latest() still resolves by channel id after the index-map change", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [9.9, 32, 34.5] });
    expect(s.latest(1)).toBe(9.9);
    expect(s.latest(999)).toBeUndefined();
  });

  it("ignores a frame whose values length mismatches channels", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [1, 2] }); // too short (meta has 3 channels)
    expect(s.latest(1)).toBeUndefined();
    expect(s.series(1)?.len()).toBe(0);
  });

  it("clears metrics and lastEmit on re-meta", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyMetrics({ type: "metrics", cpu_pct: 5, ram_mb: 90 });
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 123, values: [1, 32, 34.5] });
    s.applyMeta(meta());
    expect(s.metrics()).toBeUndefined();
    expect(s.lastEmitUnixMs()).toBe(0);
  });

  it("tracks last ride ts, rate, and duration", () => {
    const s = new TelemetryStore();
    s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 600, enum_values: [],
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" }] });
    expect(s.rateHz()).toBe(10);
    expect(s.durationMs()).toBe(600_000);
    expect(s.lastTsMs()).toBe(0);
    s.applyFrame({ type: "frame", ts_ms: 4200, emit_unix_ms: 1, values: [1] });
    expect(s.lastTsMs()).toBe(4200);
  });
});
