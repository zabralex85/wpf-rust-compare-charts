import { useEffect, useRef, useState } from "react";
import { TelemetryStore } from "../data/store";
import { FpsMeter } from "../hud/fps";
import { createWsClient, type WsStatus } from "../ws/client";

export interface TelemetrySnapshot {
  store: TelemetryStore;
  fps: number;
  frameTimeMs: number;
  status: WsStatus;
  version: number;
}

export function useTelemetry(url: string): TelemetrySnapshot {
  const storeRef = useRef<TelemetryStore | undefined>(undefined);
  const meterRef = useRef<FpsMeter | undefined>(undefined);
  if (!storeRef.current) storeRef.current = new TelemetryStore();
  if (!meterRef.current) meterRef.current = new FpsMeter();

  const [status, setStatus] = useState<WsStatus>("connecting");
  const [version, setVersion] = useState(0);
  const fpsRef = useRef(0);
  const ftRef = useRef(0);

  useEffect(() => {
    const store = storeRef.current!;
    const meter = meterRef.current!;
    const client = createWsClient({
      url,
      onMeta: (m) => store.applyMeta(m),
      onFrame: (f) => store.applyFrame(f),
      onMetrics: (m) => store.applyMetrics(m),
      onStatus: setStatus,
    });

    let raf = 0;
    const loop = (t: number) => {
      meter.tick(t);
      fpsRef.current = meter.fps();
      ftRef.current = meter.frameTimeMs();
      setVersion((v) => v + 1);
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);

    return () => {
      cancelAnimationFrame(raf);
      client.stop();
    };
  }, [url]);

  return {
    store: storeRef.current,
    fps: fpsRef.current,
    frameTimeMs: ftRef.current,
    status,
    version,
  };
}
