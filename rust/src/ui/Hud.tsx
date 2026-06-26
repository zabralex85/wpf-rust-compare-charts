import type React from "react";
import type { TelemetryStore } from "../data/store";
import { latencyMs } from "../hud/fps";

export function Hud({ store, fps, frameTimeMs, nowMs }: { store: TelemetryStore; fps: number; frameTimeMs: number; nowMs: number }): React.JSX.Element {
  const lat = store.lastEmitUnixMs() > 0 ? latencyMs(store.lastEmitUnixMs(), nowMs) : 0;
  const m = store.metrics();
  return (
    <div className="hud">
      <div>FPS <b>{fps.toFixed(0)}</b></div>
      <div>frame <b>{frameTimeMs.toFixed(1)}</b> ms</div>
      <div>latency <b>{lat.toFixed(0)}</b> ms</div>
      <div>CPU <b>{m ? m.cpu_pct.toFixed(1) : "-"}</b> %</div>
      <div>RAM <b>{m ? m.ram_mb.toFixed(0) : "-"}</b> MB</div>
    </div>
  );
}
