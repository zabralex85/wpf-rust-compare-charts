import type { ChannelMeta } from "../../types";
import type { TelemetryStore } from "../../data/store";
import { formatValue, decodeEnum, severityColor } from "../../format/value";

export interface ParamRow { text: string; valueColor: string; dotColor: string; critical: boolean; }

export function paramRow(ch: ChannelMeta, store: TelemetryStore): ParamRow {
  const idx = store.enumIndex();
  const v = store.latest(ch.id);
  if (v === undefined) {
    return { text: "—", valueColor: "var(--dim)", dotColor: severityColor(undefined), critical: false };
  }
  const text = formatValue(ch, v, idx);
  let severity: string | undefined;
  if (ch.type === "enum") {
    severity = decodeEnum(ch.id, v, idx)?.severity;
  } else if (ch.max > ch.min) {
    severity = v <= ch.min || v >= ch.max ? "critical" : "ok";
  } else {
    severity = "ok";
  }
  const critical = severity === "critical";
  const dotColor = severityColor(severity);
  const valueColor = critical ? severityColor("critical")
    : severity === "ok" && ch.type === "enum" ? "var(--green2)"
    : "var(--text)";
  return { text, valueColor, dotColor, critical };
}
