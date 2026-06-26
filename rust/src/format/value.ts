import type { ChannelMeta, EnumValue } from "../types";

export function buildEnumIndex(enums: EnumValue[]): Map<string, EnumValue> {
  const m = new Map<string, EnumValue>();
  for (const e of enums) m.set(`${e.channel_id}:${e.code}`, e);
  return m;
}

export function decodeEnum(channelId: number, value: number, index: Map<string, EnumValue>): EnumValue | undefined {
  return index.get(`${channelId}:${Math.round(value)}`);
}

export function formatValue(ch: ChannelMeta, value: number, index: Map<string, EnumValue>): string {
  if (ch.type === "enum") {
    return decodeEnum(ch.id, value, index)?.label ?? String(Math.round(value));
  }
  if (ch.type === "real") {
    return value.toFixed(3);
  }
  return String(value);
}

export function severityColor(severity: string | undefined): string {
  switch (severity) {
    case "critical": return "#d22";
    case "ok": return "#2a2";
    default: return "#888";
  }
}
