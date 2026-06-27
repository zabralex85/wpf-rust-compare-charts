import type { ChannelMeta } from "../../types";

const GROUPS: { group: string; cols: string[] }[] = [
  { group: "INU Mode", cols: ["inu_mode1", "inu_mode2"] },
  { group: "Velocity", cols: ["vel_x", "vel_y", "vel_z", "plat_azim", "vclimb"] },
  { group: "Attitude", cols: ["roll", "pitch", "heading_t", "heading_m", "sky_pitch", "sky_roll", "sky_azim", "sky_heading", "prsnt_head"] },
  { group: "Acceleration", cols: ["acc_x", "acc_y", "acc_z"] },
  { group: "Body Rates", cols: ["roll_r", "pitch_r", "yaw_r"] },
  { group: "Position", cols: ["lat", "lon"] },
];

export const GROUP_OF: Record<string, string> = {};
for (const g of GROUPS) for (const c of g.cols) GROUP_OF[c] = g.group;

const ORDER = [...GROUPS.map((g) => g.group), "System"];

export function groupOf(columnName: string): string {
  return GROUP_OF[columnName] ?? "System";
}

export function groupChannels(channels: ChannelMeta[]): { group: string; channels: ChannelMeta[] }[] {
  const buckets = new Map<string, ChannelMeta[]>();
  for (const ch of channels) {
    const g = groupOf(ch.column_name);
    (buckets.get(g) ?? buckets.set(g, []).get(g)!).push(ch);
  }
  return ORDER.flatMap((group) => {
    const list = buckets.get(group);
    if (!list || list.length === 0) return [];
    list.sort((a, b) => a.display_order - b.display_order);
    return [{ group, channels: list }];
  });
}
