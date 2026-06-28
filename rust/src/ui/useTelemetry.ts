import { useEffect, useRef, useState } from "react";
import { TelemetryStore } from "../data/store";
import { FpsMeter } from "../hud/fps";
import { createWsClient, type WsStatus } from "../ws/client";
import { applyMockSnapshot } from "./mock/fixture";

export interface TelemetrySnapshot {
  store: TelemetryStore;
  fps: number;
  frameTimeMs: number;
  status: WsStatus;
  version: number;
  send: (json: string) => void;
}

export function useTelemetry(url: string): TelemetrySnapshot {
  const storeRef = useRef<TelemetryStore | undefined>(undefined);
  const meterRef = useRef<FpsMeter | undefined>(undefined);
  // Stable ref for the ws send function; no-op until a live client is created.
  const sendRef = useRef<(json: string) => void>(() => {});

  // Detect mock mode once (pure read of URL, safe in render).
  const isMock =
    import.meta.env.DEV &&
    typeof window !== "undefined" &&
    new URLSearchParams(window.location.search).has("mock");

  // Apply mock snapshot synchronously on first store creation so that
  // child components (e.g., WidgetGrid → useWidgets) see populated channels
  // on their very first mount.  The useEffect below re-applies it (idempotent).
  if (!storeRef.current) {
    storeRef.current = new TelemetryStore();
    if (isMock) applyMockSnapshot(storeRef.current);
  }
  if (!meterRef.current) meterRef.current = new FpsMeter();

  const [status, setStatus] = useState<WsStatus>(isMock ? "open" : "connecting");
  const [version, setVersion] = useState(0);
  const fpsRef = useRef(0);
  const ftRef = useRef(0);

  useEffect(() => {
    const store = storeRef.current!;
    const meter = meterRef.current!;
    const mock = isMock;

    let client: ReturnType<typeof createWsClient> | undefined;
    if (mock) {
      applyMockSnapshot(store);
      setStatus("open");
      // mock mode: send is a no-op (leave sendRef as-is)
    } else {
      client = createWsClient({
        url,
        onMeta: (m) => store.applyMeta(m),
        onFrame: (f) => store.applyFrame(f),
        onMetrics: (m) => store.applyMetrics(m),
        onStatus: setStatus,
      });
      sendRef.current = (json) => client!.send(json);
    }

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
      client?.stop();
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url]); // isMock is a constant derived from window.location (never changes)

  return {
    store: storeRef.current,
    fps: fpsRef.current,
    frameTimeMs: ftRef.current,
    status,
    version,
    send: (json) => sendRef.current(json),
  };
}
