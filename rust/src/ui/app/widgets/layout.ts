import type { ChannelMeta } from "../../../types";

/**
 * A descriptor for a single widget slot in the overview dashboard grid.
 *
 * id scheme:
 *   map widget    → "map"
 *   gauge widget  → "gauge-<channelId>"
 *   line widget   → "line-<channelId>"
 *
 * Ordering: map → gauges (display_order asc) → lines (roll first, acc_z second).
 */
export type Widget = {
  id: string;
  kind: "gauge" | "line" | "map";
  channelId?: number;
  name: string;
  cols: number;
  rows: number;
};

/** Key strip channels included as line widgets, in this fixed order. */
const LINE_CHANNEL_NAMES = ["roll", "acc_z"] as const;

/**
 * Builds the default widget-layout descriptor for the overview dashboard.
 *
 * Selection rules (all deterministic, no random/Date):
 *  1. map  (cols 4, rows 4) — only when BOTH a map_lat AND a map_lon channel exist.
 *  2. gauge (cols 1, rows 1) — one per channel where `widget === "gauge"`,
 *     ordered by display_order ascending.
 *  3. line  (cols 2, rows 1) — one for "roll" then one for "acc_z", only if
 *     a strip channel with that column_name actually exists in `channels`.
 *
 * Returns an empty array when `channels` is empty.
 */
export function defaultWidgets(channels: ChannelMeta[]): Widget[] {
  const result: Widget[] = [];

  // ── 1. map ────────────────────────────────────────────────────────────────
  const hasLat = channels.some((c) => c.widget === "map_lat");
  const hasLon = channels.some((c) => c.widget === "map_lon");

  if (hasLat && hasLon) {
    result.push({
      id: "map",
      kind: "map",
      name: "Flight Track",
      cols: 4,
      rows: 4,
    });
  }

  // ── 2. gauges ─────────────────────────────────────────────────────────────
  const gaugeChannels = channels
    .filter((c) => c.widget === "gauge")
    .slice()
    .sort((a, b) => a.display_order - b.display_order);

  for (const ch of gaugeChannels) {
    result.push({
      id: `gauge-${ch.id}`,
      kind: "gauge",
      channelId: ch.id,
      name: ch.name,
      cols: 1,
      rows: 1,
    });
  }

  // ── 3. lines ──────────────────────────────────────────────────────────────
  for (const colName of LINE_CHANNEL_NAMES) {
    const ch = channels.find(
      (c) => c.column_name === colName && c.widget === "strip",
    );
    if (ch) {
      result.push({
        id: `line-${ch.id}`,
        kind: "line",
        channelId: ch.id,
        name: ch.name,
        cols: 2,
        rows: 1,
      });
    }
  }

  return result;
}
