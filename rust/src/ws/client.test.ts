import { describe, it, expect, vi } from "vitest";
import { createWsClient, type SocketLike } from "./client";

class FakeSocket implements SocketLike {
  onopen: (() => void) | null = null;
  onmessage: ((ev: { data: string }) => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  close() { this.closed = true; }
  emit(data: unknown) { this.onmessage?.({ data: JSON.stringify(data) }); }
}

describe("createWsClient", () => {
  it("routes meta/frame/metrics to the right callbacks", () => {
    const sock = new FakeSocket();
    const onMeta = vi.fn(), onFrame = vi.fn(), onMetrics = vi.fn();
    createWsClient({ url: "ws://x", onMeta, onFrame, onMetrics, socketFactory: () => sock });
    sock.onopen?.();
    sock.emit({ type: "meta", channels: [], enum_values: [], rate_hz: 10 });
    sock.emit({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [1] });
    sock.emit({ type: "metrics", cpu_pct: 1, ram_mb: 2 });
    expect(onMeta).toHaveBeenCalledOnce();
    expect(onFrame).toHaveBeenCalledOnce();
    expect(onMetrics).toHaveBeenCalledOnce();
  });

  it("reconnects on close via injected scheduler", () => {
    const sockets: FakeSocket[] = [];
    const factory = () => { const s = new FakeSocket(); sockets.push(s); return s; };
    const schedule = (fn: () => void) => { fn(); return 0; }; // run immediately
    createWsClient({ url: "ws://x", onMeta: vi.fn(), onFrame: vi.fn(), onMetrics: vi.fn(), socketFactory: factory, schedule });
    sockets[0].onclose?.();
    expect(sockets.length).toBe(2); // reconnected
  });

  it("stop() prevents reconnect", () => {
    const sockets: FakeSocket[] = [];
    const factory = () => { const s = new FakeSocket(); sockets.push(s); return s; };
    const schedule = (fn: () => void) => { fn(); return 0; };
    const client = createWsClient({ url: "ws://x", onMeta: vi.fn(), onFrame: vi.fn(), onMetrics: vi.fn(), socketFactory: factory, schedule });
    client.stop();
    sockets[0].onclose?.();
    expect(sockets.length).toBe(1); // no reconnect after stop
  });
});
