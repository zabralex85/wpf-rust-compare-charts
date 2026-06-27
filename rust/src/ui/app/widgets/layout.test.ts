import { describe, it, expect } from "vitest";
import type { ChannelMeta } from "../../../types";
import { defaultWidgets } from "./layout";

// ── helpers ──────────────────────────────────────────────────────────────────

function ch(
  id: number,
  column_name: string,
  widget: ChannelMeta["widget"],
  display_order: number,
  name?: string,
): ChannelMeta {
  return {
    id,
    name: name ?? column_name,
    column_name,
    unit: "",
    type: "real",
    min: 0,
    max: 100,
    widget,
    display_order,
    addr: "I_01",
  };
}

// Full fixture: lat + lon + 2 gauges + roll + acc_z + extras
const FULL_CHANNELS: ChannelMeta[] = [
  ch(1, "gps_lat", "map_lat", 1),
  ch(2, "gps_lon", "map_lon", 2),
  ch(3, "sky_pitch", "gauge", 3),
  ch(4, "sky_roll", "gauge", 4),
  ch(5, "roll", "strip", 5),
  ch(6, "acc_z", "strip", 6),
  ch(7, "temp", "table", 7),
];

// No-GPS fixture (no map_lat / map_lon)
const NO_GPS_CHANNELS: ChannelMeta[] = [
  ch(3, "sky_pitch", "gauge", 3),
  ch(4, "sky_roll", "gauge", 4),
  ch(5, "roll", "strip", 5),
  ch(6, "acc_z", "strip", 6),
];

// Missing strips fixture
const NO_STRIP_CHANNELS: ChannelMeta[] = [
  ch(1, "gps_lat", "map_lat", 1),
  ch(2, "gps_lon", "map_lon", 2),
  ch(3, "sky_pitch", "gauge", 3),
];

// ── full fixture suite ────────────────────────────────────────────────────────

describe("defaultWidgets – full fixture", () => {
  const widgets = defaultWidgets(FULL_CHANNELS);

  it("returns an array", () => {
    expect(Array.isArray(widgets)).toBe(true);
  });

  it("all ids are unique", () => {
    const ids = widgets.map((w) => w.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("map-first ordering: first widget is kind=map", () => {
    expect(widgets[0].kind).toBe("map");
  });

  it("exactly one map widget", () => {
    expect(widgets.filter((w) => w.kind === "map")).toHaveLength(1);
  });

  it("map widget has cols=4 rows=4", () => {
    const map = widgets.find((w) => w.kind === "map")!;
    expect(map.cols).toBe(4);
    expect(map.rows).toBe(4);
  });

  it("map widget has no channelId (uses both GPS channels)", () => {
    const map = widgets.find((w) => w.kind === "map")!;
    expect(map.channelId).toBeUndefined();
  });

  it("map widget id is 'map'", () => {
    const map = widgets.find((w) => w.kind === "map")!;
    expect(map.id).toBe("map");
  });

  it("gauge widgets: one per gauge-widget channel", () => {
    const gauges = widgets.filter((w) => w.kind === "gauge");
    expect(gauges).toHaveLength(2);
  });

  it("gauge widgets have cols=1 rows=1", () => {
    widgets.filter((w) => w.kind === "gauge").forEach((g) => {
      expect(g.cols).toBe(1);
      expect(g.rows).toBe(1);
    });
  });

  it("gauge channelIds match the source channel ids (sky_pitch=3, sky_roll=4)", () => {
    const gauges = widgets.filter((w) => w.kind === "gauge");
    const ids = gauges.map((g) => g.channelId);
    expect(ids).toContain(3);
    expect(ids).toContain(4);
  });

  it("gauge ids follow 'gauge-<channelId>' scheme", () => {
    widgets.filter((w) => w.kind === "gauge").forEach((g) => {
      expect(g.id).toBe(`gauge-${g.channelId}`);
    });
  });

  it("gauge widgets appear after the map widget", () => {
    const mapIdx = widgets.findIndex((w) => w.kind === "map");
    widgets.filter((w) => w.kind === "gauge").forEach((g) => {
      expect(widgets.indexOf(g)).toBeGreaterThan(mapIdx);
    });
  });

  it("line widgets: roll and acc_z both present → 2 line widgets", () => {
    const lines = widgets.filter((w) => w.kind === "line");
    expect(lines).toHaveLength(2);
  });

  it("line widgets have cols=2 rows=1", () => {
    widgets.filter((w) => w.kind === "line").forEach((l) => {
      expect(l.cols).toBe(2);
      expect(l.rows).toBe(1);
    });
  });

  it("line channelIds match roll=5 and acc_z=6", () => {
    const lines = widgets.filter((w) => w.kind === "line");
    const ids = lines.map((l) => l.channelId);
    expect(ids).toContain(5);
    expect(ids).toContain(6);
  });

  it("line ids follow 'line-<channelId>' scheme", () => {
    widgets.filter((w) => w.kind === "line").forEach((l) => {
      expect(l.id).toBe(`line-${l.channelId}`);
    });
  });

  it("roll line widget comes before acc_z line widget (fixed order)", () => {
    const lines = widgets.filter((w) => w.kind === "line");
    expect(lines[0].channelId).toBe(5); // roll
    expect(lines[1].channelId).toBe(6); // acc_z
  });

  it("line widgets appear after gauge widgets", () => {
    const lastGaugeIdx = Math.max(
      ...widgets.filter((w) => w.kind === "gauge").map((g) => widgets.indexOf(g)),
    );
    widgets.filter((w) => w.kind === "line").forEach((l) => {
      expect(widgets.indexOf(l)).toBeGreaterThan(lastGaugeIdx);
    });
  });

  it("overall order: map → gauge → line", () => {
    const kinds = widgets.map((w) => w.kind);
    // map first, then all gauges, then all lines (no interleaving)
    const mapEnd = kinds.lastIndexOf("map");
    const gaugeEnd = kinds.lastIndexOf("gauge");
    const lineStart = kinds.indexOf("line");
    expect(mapEnd).toBeLessThan(gaugeEnd);
    expect(gaugeEnd).toBeLessThan(lineStart);
  });

  it("gauge widgets are in display_order (sky_pitch before sky_roll)", () => {
    const gauges = widgets.filter((w) => w.kind === "gauge");
    expect(gauges[0].channelId).toBe(3); // sky_pitch display_order=3
    expect(gauges[1].channelId).toBe(4); // sky_roll display_order=4
  });

  it("widget names are non-empty strings", () => {
    widgets.forEach((w) => {
      expect(typeof w.name).toBe("string");
      expect(w.name.length).toBeGreaterThan(0);
    });
  });
});

// ── no-GPS fixture ────────────────────────────────────────────────────────────

describe("defaultWidgets – no GPS channels", () => {
  const widgets = defaultWidgets(NO_GPS_CHANNELS);

  it("no map widget when map_lat/map_lon absent", () => {
    expect(widgets.filter((w) => w.kind === "map")).toHaveLength(0);
  });

  it("still returns gauge and line widgets", () => {
    expect(widgets.filter((w) => w.kind === "gauge")).toHaveLength(2);
    expect(widgets.filter((w) => w.kind === "line")).toHaveLength(2);
  });

  it("all ids still unique", () => {
    const ids = widgets.map((w) => w.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});

// ── only map_lat, no map_lon → no map ────────────────────────────────────────

describe("defaultWidgets – map_lat only (no map_lon)", () => {
  const channels = [ch(1, "gps_lat", "map_lat", 1)];

  it("requires BOTH map_lat and map_lon to produce a map widget", () => {
    const widgets = defaultWidgets(channels);
    expect(widgets.filter((w) => w.kind === "map")).toHaveLength(0);
  });
});

// ── missing strip channels ────────────────────────────────────────────────────

describe("defaultWidgets – no strip channels (roll/acc_z absent)", () => {
  const widgets = defaultWidgets(NO_STRIP_CHANNELS);

  it("no line widgets when roll and acc_z absent", () => {
    expect(widgets.filter((w) => w.kind === "line")).toHaveLength(0);
  });

  it("map and gauge still present", () => {
    expect(widgets.filter((w) => w.kind === "map")).toHaveLength(1);
    expect(widgets.filter((w) => w.kind === "gauge")).toHaveLength(1);
  });
});

// ── partial strips (only roll present) ───────────────────────────────────────

describe("defaultWidgets – only roll strip present", () => {
  const channels = [
    ch(1, "gps_lat", "map_lat", 1),
    ch(2, "gps_lon", "map_lon", 2),
    ch(5, "roll", "strip", 5),
  ];
  const widgets = defaultWidgets(channels);

  it("exactly one line widget (roll), acc_z absent so skipped", () => {
    const lines = widgets.filter((w) => w.kind === "line");
    expect(lines).toHaveLength(1);
    expect(lines[0].channelId).toBe(5);
  });
});

// ── empty input ───────────────────────────────────────────────────────────────

describe("defaultWidgets – empty channels array", () => {
  it("returns empty array", () => {
    expect(defaultWidgets([])).toEqual([]);
  });
});
