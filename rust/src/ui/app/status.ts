import type { TelemetryStore } from "../../data/store";
import { decodeEnum } from "../../format/value";

export interface SystemStatus { alarms: number; cautions: number; linkOk: boolean; }

export function deriveStatus(store: TelemetryStore): SystemStatus {
  const idx = store.enumIndex();
  let alarms = 0, cautions = 0;
  for (const ch of store.channels()) {
    if (ch.type !== "enum") continue;
    const v = store.latest(ch.id);
    if (v === undefined) continue;
    const sev = decodeEnum(ch.id, v, idx)?.severity;
    if (sev === "critical") alarms++;
    else if (sev === "caution") cautions++;
  }
  return { alarms, cautions, linkOk: store.channels().length > 0 };
}
