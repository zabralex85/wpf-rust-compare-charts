import { decodeMessage } from "./decode";
import type { MetaMessage, FrameMessage, MetricsMessage } from "../types";

export interface SocketLike {
  onopen: (() => void) | null;
  onmessage: ((ev: { data: string }) => void) | null;
  onclose: (() => void) | null;
  onerror: (() => void) | null;
  send(data: string): void;
  close(): void;
}

export type WsStatus = "connecting" | "open" | "closed";

export interface WsClientOptions {
  url: string;
  onMeta: (m: MetaMessage) => void;
  onFrame: (f: FrameMessage) => void;
  onMetrics: (m: MetricsMessage) => void;
  onStatus?: (s: WsStatus) => void;
  socketFactory?: (url: string) => SocketLike;
  schedule?: (fn: () => void, ms: number) => unknown;
  reconnectDelayMs?: number;
}

export function createWsClient(opts: WsClientOptions): { stop(): void; send(json: string): void } {
  const factory = opts.socketFactory ?? ((u) => new WebSocket(u) as unknown as SocketLike);
  const schedule = opts.schedule ?? ((fn, ms) => setTimeout(fn, ms));
  const delay = opts.reconnectDelayMs ?? 1000;
  let stopped = false;
  let current: SocketLike | null = null;

  const connect = () => {
    opts.onStatus?.("connecting");
    const sock = factory(opts.url);
    current = sock;
    sock.onopen = () => opts.onStatus?.("open");
    sock.onmessage = (ev) => {
      const msg = decodeMessage(ev.data);
      if (msg.type === "meta") opts.onMeta(msg);
      else if (msg.type === "frame") opts.onFrame(msg);
      else if (msg.type === "metrics") opts.onMetrics(msg);
    };
    sock.onclose = () => {
      opts.onStatus?.("closed");
      if (!stopped) schedule(connect, delay);
    };
    sock.onerror = () => sock.close();
  };

  connect();
  return {
    stop() { stopped = true; current?.close(); },
    // WebSocket.send() throws if the socket is CONNECTING/CLOSED (e.g. clicking a
    // transport control before the WS connects, or during the reconnect gap) —
    // swallow it so a command sent off-window is a no-op, not an exception.
    send(json: string) { try { current?.send(json); } catch { /* socket not open */ } },
  };
}
