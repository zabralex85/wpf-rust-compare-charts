export interface ChannelMeta {
  id: number;
  name: string;
  column_name: string;
  unit: string;
  type: "real" | "enum" | "hex" | "text" | "time";
  min: number;
  max: number;
  widget: "strip" | "gauge" | "table" | "map_lat" | "map_lon";
  display_order: number;
  addr: string;
}

export interface EnumValue {
  channel_id: number;
  code: number;
  label: string;
  severity: string;
}

export interface MetaMessage {
  type: "meta";
  channels: ChannelMeta[];
  enum_values: EnumValue[];
  rate_hz: number;
  duration_s: number;
}

export interface FrameMessage {
  type: "frame";
  ts_ms: number;
  emit_unix_ms: number;
  values: number[];
}

export interface MetricsMessage {
  type: "metrics";
  cpu_pct: number;
  ram_mb: number;
}

export type WsMessage = MetaMessage | FrameMessage | MetricsMessage;
