# Rust Frontend — Data/Logic Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure-TypeScript data layer for the React dashboard: decode the backend WebSocket protocol, buffer time-series, maintain telemetry state, and provide the formatting + HUD + gauge math the UI widgets (Plan 5) will render.

**Architecture:** Plain TS modules under `rust/src/`, each with one responsibility and a `vitest` unit test: `types` (wire types), `ws/decode` + `ws/client` (protocol), `data/ringBuffer` (windowed series) + `data/store` (telemetry state), `format/value` (display + enum), `hud/fps` (FPS/latency math), `gauge/geometry` (needle angle). No React, no DOM — every module is testable in a Node environment. Plan 5 imports these into components.

**Tech Stack:** TypeScript (strict), Vite (already scaffolded under `rust/`), vitest. No charting libs here (Plan 5 adds uPlot/Leaflet).

## Global Constraints

- Consume the Plan-2 backend wire contract on `ws://127.0.0.1:9001`, exactly:
  - `{"type":"meta", channels: ChannelMeta[], enum_values: EnumValue[], rate_hz: number}` (once on connect)
  - `{"type":"frame", ts_ms: number, emit_unix_ms: number, values: number[]}` (streamed; `values` index-aligned to `channels` sorted by `display_order`)
  - `{"type":"metrics", cpu_pct: number, ram_mb: number}` (~once per ride-second)
- `ChannelMeta` fields (serde JSON from backend): `id, name, column_name, unit, type, min, max, widget, display_order, addr`. `EnumValue`: `channel_id, code, label, severity`.
- End-to-end latency = `Date.now() - emit_unix_ms` (ms).
- Strip-chart window default = **60_000 ms**.
- TypeScript strict mode; no `any` in exported signatures.
- Pure logic only — no React imports, no `document`/`window` globals except where a narrow injectable seam is provided (WebSocket factory, timer, `now()`).

---

### Task 1: Add vitest + test scaffolding

**Files:**
- Modify: `rust/package.json` (devDeps + `test` script)
- Create: `rust/vitest.config.ts`
- Create: `rust/src/sanity.test.ts` (trivial, proves the runner works; deleted at end of task)

**Interfaces:**
- Produces: a working `npm test` (`vitest run`) in `rust/` using the Node environment.

- [ ] **Step 1: Install vitest**

Run (from `rust/`):
```bash
npm install -D vitest
```

- [ ] **Step 2: Add the test script**

In `rust/package.json` `"scripts"`, add:
```json
"test": "vitest run",
"test:watch": "vitest"
```

- [ ] **Step 3: Vitest config (Node environment)**

```ts
// rust/vitest.config.ts
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
```

- [ ] **Step 4: Sanity test proves the runner works**

```ts
// rust/src/sanity.test.ts
import { describe, it, expect } from "vitest";

describe("vitest", () => {
  it("runs", () => {
    expect(1 + 1).toBe(2);
  });
});
```

Run (from `rust/`): `npm test`
Expected: 1 passed.

- [ ] **Step 5: Remove the sanity test and commit**

```bash
rm src/sanity.test.ts
git add rust/package.json rust/package-lock.json rust/vitest.config.ts
git commit -m "test(rust-fe): add vitest with node environment"
```

---

### Task 2: Wire types + message decode

**Files:**
- Create: `rust/src/types.ts`
- Create: `rust/src/ws/decode.ts`
- Test: `rust/src/ws/decode.test.ts`

**Interfaces:**
- Produces:
  - `types.ts`: `ChannelMeta`, `EnumValue`, `MetaMessage`, `FrameMessage`, `MetricsMessage`, and `WsMessage = MetaMessage | FrameMessage | MetricsMessage`
  - `decode.ts`: `decodeMessage(json: string): WsMessage` — parses + validates the `type` tag; throws `Error` on malformed/unknown messages

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ws/decode.test.ts
import { describe, it, expect } from "vitest";
import { decodeMessage } from "./decode";

describe("decodeMessage", () => {
  it("decodes a meta message", () => {
    const json = JSON.stringify({
      type: "meta",
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" }],
      enum_values: [{ channel_id: 15, code: 1, label: "Critical", severity: "critical" }],
      rate_hz: 10,
    });
    const m = decodeMessage(json);
    expect(m.type).toBe("meta");
    if (m.type === "meta") {
      expect(m.channels[0].column_name).toBe("roll");
      expect(m.rate_hz).toBe(10);
    }
  });

  it("decodes a frame message", () => {
    const m = decodeMessage(JSON.stringify({ type: "frame", ts_ms: 100, emit_unix_ms: 1700000000000, values: [1.5, 2] }));
    expect(m.type).toBe("frame");
    if (m.type === "frame") expect(m.values).toEqual([1.5, 2]);
  });

  it("decodes a metrics message", () => {
    const m = decodeMessage(JSON.stringify({ type: "metrics", cpu_pct: 12.5, ram_mb: 80 }));
    expect(m.type).toBe("metrics");
    if (m.type === "metrics") expect(m.ram_mb).toBe(80);
  });

  it("throws on unknown type", () => {
    expect(() => decodeMessage(JSON.stringify({ type: "nope" }))).toThrow();
  });

  it("throws on invalid json", () => {
    expect(() => decodeMessage("{not json")).toThrow();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `rust/`): `npm test`
Expected: FAIL — cannot find module `./decode`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/types.ts
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
```

```ts
// rust/src/ws/decode.ts
import type { WsMessage } from "../types";

export function decodeMessage(json: string): WsMessage {
  const obj = JSON.parse(json) as { type?: unknown };
  switch (obj.type) {
    case "meta":
    case "frame":
    case "metrics":
      return obj as WsMessage;
    default:
      throw new Error(`unknown message type: ${String(obj.type)}`);
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `rust/`): `npm test`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add rust/src/types.ts rust/src/ws/decode.ts rust/src/ws/decode.test.ts
git commit -m "feat(rust-fe): wire types + message decoder"
```

---

### Task 3: WebSocket client with reconnect

**Files:**
- Create: `rust/src/ws/client.ts`
- Test: `rust/src/ws/client.test.ts`

**Interfaces:**
- Consumes: `decodeMessage`, `MetaMessage`, `FrameMessage`, `MetricsMessage`
- Produces:
  - `SocketLike` interface (the subset of `WebSocket` used: `onopen`, `onmessage`, `onclose`, `onerror`, `close()`) so tests can inject a fake
  - `WsClientOptions { url: string; onMeta; onFrame; onMetrics; onStatus?; socketFactory?; schedule?; reconnectDelayMs? }`
  - `createWsClient(opts: WsClientOptions): { stop(): void }` — connects, routes decoded messages to callbacks, and reconnects on close via the injectable `schedule` (defaults to `setTimeout`)

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ws/client.test.ts
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./client`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/ws/client.ts
import { decodeMessage } from "./decode";
import type { MetaMessage, FrameMessage, MetricsMessage } from "../types";

export interface SocketLike {
  onopen: (() => void) | null;
  onmessage: ((ev: { data: string }) => void) | null;
  onclose: (() => void) | null;
  onerror: (() => void) | null;
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

export function createWsClient(opts: WsClientOptions): { stop(): void } {
  const factory = opts.socketFactory ?? ((u) => new WebSocket(u) as unknown as SocketLike);
  const schedule = opts.schedule ?? ((fn, ms) => setTimeout(fn, ms));
  const delay = opts.reconnectDelayMs ?? 1000;
  let stopped = false;

  const connect = () => {
    opts.onStatus?.("connecting");
    const sock = factory(opts.url);
    sock.onopen = () => opts.onStatus?.("open");
    sock.onmessage = (ev) => {
      const msg = decodeMessage(ev.data);
      if (msg.type === "meta") opts.onMeta(msg);
      else if (msg.type === "frame") opts.onFrame(msg);
      else opts.onMetrics(msg);
    };
    sock.onclose = () => {
      opts.onStatus?.("closed");
      if (!stopped) schedule(connect, delay);
    };
    sock.onerror = () => sock.close();
  };

  connect();
  return { stop() { stopped = true; } };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS (3 client tests + earlier suites).

- [ ] **Step 5: Commit**

```bash
git add rust/src/ws/client.ts rust/src/ws/client.test.ts
git commit -m "feat(rust-fe): websocket client with injectable socket + reconnect"
```

---

### Task 4: Windowed ring buffer for strip charts

**Files:**
- Create: `rust/src/data/ringBuffer.ts`
- Test: `rust/src/data/ringBuffer.test.ts`

**Interfaces:**
- Produces:
  - `class ChannelSeries` — `constructor(windowMs: number)`, `push(tsMs: number, value: number): void` (drops samples older than `latestTs - windowMs`), `arrays(): { xs: number[]; ys: number[] }`, `len(): number`

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/data/ringBuffer.test.ts
import { describe, it, expect } from "vitest";
import { ChannelSeries } from "./ringBuffer";

describe("ChannelSeries", () => {
  it("keeps samples within the window and evicts older ones", () => {
    const s = new ChannelSeries(1000); // 1s window
    s.push(0, 10);
    s.push(500, 11);
    s.push(1000, 12);
    s.push(1600, 13); // window now [600,1600] -> drops ts=0 and ts=500
    const { xs, ys } = s.arrays();
    expect(xs).toEqual([1000, 1600]);
    expect(ys).toEqual([12, 13]);
  });

  it("arrays() returns parallel xs/ys of equal length", () => {
    const s = new ChannelSeries(10_000);
    s.push(0, 1);
    s.push(100, 2);
    const { xs, ys } = s.arrays();
    expect(xs.length).toBe(ys.length);
    expect(s.len()).toBe(2);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./ringBuffer`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/data/ringBuffer.ts
export class ChannelSeries {
  private xs: number[] = [];
  private ys: number[] = [];
  constructor(private readonly windowMs: number) {}

  push(tsMs: number, value: number): void {
    this.xs.push(tsMs);
    this.ys.push(value);
    const cutoff = tsMs - this.windowMs;
    let drop = 0;
    while (drop < this.xs.length && this.xs[drop] < cutoff) drop++;
    if (drop > 0) {
      this.xs.splice(0, drop);
      this.ys.splice(0, drop);
    }
  }

  arrays(): { xs: number[]; ys: number[] } {
    return { xs: this.xs, ys: this.ys };
  }

  len(): number {
    return this.xs.length;
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/data/ringBuffer.ts rust/src/data/ringBuffer.test.ts
git commit -m "feat(rust-fe): time-windowed channel series buffer"
```

---

### Task 5: Value formatting + enum decode

**Files:**
- Create: `rust/src/format/value.ts`
- Test: `rust/src/format/value.test.ts`

**Interfaces:**
- Consumes: `ChannelMeta`, `EnumValue`
- Produces:
  - `buildEnumIndex(enums: EnumValue[]): Map<string, EnumValue>` — keyed by `` `${channel_id}:${code}` ``
  - `decodeEnum(channelId: number, value: number, index: Map<string, EnumValue>): EnumValue | undefined`
  - `formatValue(ch: ChannelMeta, value: number, index: Map<string, EnumValue>): string` — enum → label, real → 3 decimals, else number string
  - `severityColor(severity: string | undefined): string` — `"critical"`→`"#d22"`, `"ok"`→`"#2a2"`, default `"#888"`

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/format/value.test.ts
import { describe, it, expect } from "vitest";
import { buildEnumIndex, decodeEnum, formatValue, severityColor } from "./value";
import type { ChannelMeta, EnumValue } from "../types";

const enums: EnumValue[] = [
  { channel_id: 15, code: 0, label: "Normal", severity: "ok" },
  { channel_id: 15, code: 1, label: "Critical", severity: "critical" },
];
const idx = buildEnumIndex(enums);

function ch(partial: Partial<ChannelMeta>): ChannelMeta {
  return { id: 1, name: "x", column_name: "x", unit: "", type: "real", min: 0, max: 1, widget: "table", display_order: 1, addr: "", ...partial };
}

describe("value formatting", () => {
  it("decodes enum codes", () => {
    expect(decodeEnum(15, 1, idx)?.label).toBe("Critical");
    expect(decodeEnum(15, 9, idx)).toBeUndefined();
  });

  it("formats enum channel as label", () => {
    expect(formatValue(ch({ id: 15, type: "enum" }), 1, idx)).toBe("Critical");
  });

  it("formats real channel to 3 decimals", () => {
    expect(formatValue(ch({ type: "real" }), 1.23456, idx)).toBe("1.235");
  });

  it("maps severity to color", () => {
    expect(severityColor("critical")).toBe("#d22");
    expect(severityColor("ok")).toBe("#2a2");
    expect(severityColor(undefined)).toBe("#888");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./value`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/format/value.ts
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/format/value.ts rust/src/format/value.test.ts
git commit -m "feat(rust-fe): value formatting + enum decode + severity color"
```

---

### Task 6: HUD math — FPS meter + latency

**Files:**
- Create: `rust/src/hud/fps.ts`
- Test: `rust/src/hud/fps.test.ts`

**Interfaces:**
- Produces:
  - `class FpsMeter` — `constructor(windowSize = 60)`, `tick(tsMs: number): void` (records a frame timestamp), `fps(): number` (frames/sec over the recorded window), `frameTimeMs(): number` (avg interval); returns 0 until ≥2 ticks
  - `latencyMs(emitUnixMs: number, nowMs: number): number` — `nowMs - emitUnixMs`

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/hud/fps.test.ts
import { describe, it, expect } from "vitest";
import { FpsMeter, latencyMs } from "./fps";

describe("FpsMeter", () => {
  it("returns 0 before two ticks", () => {
    const m = new FpsMeter();
    expect(m.fps()).toBe(0);
    m.tick(0);
    expect(m.fps()).toBe(0);
  });

  it("computes ~60fps from 16.67ms intervals", () => {
    const m = new FpsMeter();
    for (let i = 0; i <= 10; i++) m.tick(i * (1000 / 60));
    expect(Math.round(m.fps())).toBe(60);
    expect(m.frameTimeMs()).toBeCloseTo(1000 / 60, 1);
  });

  it("respects the window size (keeps only recent frames)", () => {
    const m = new FpsMeter(3);
    for (let i = 0; i < 10; i++) m.tick(i * 10);
    // only last 3 timestamps retained -> 2 intervals of 10ms -> 100fps
    expect(Math.round(m.fps())).toBe(100);
  });
});

describe("latencyMs", () => {
  it("is now minus emit", () => {
    expect(latencyMs(1000, 1075)).toBe(75);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./fps`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/hud/fps.ts
export class FpsMeter {
  private ts: number[] = [];
  constructor(private readonly windowSize = 60) {}

  tick(tsMs: number): void {
    this.ts.push(tsMs);
    if (this.ts.length > this.windowSize) this.ts.shift();
  }

  fps(): number {
    if (this.ts.length < 2) return 0;
    const span = this.ts[this.ts.length - 1] - this.ts[0];
    if (span <= 0) return 0;
    return ((this.ts.length - 1) * 1000) / span;
  }

  frameTimeMs(): number {
    if (this.ts.length < 2) return 0;
    const span = this.ts[this.ts.length - 1] - this.ts[0];
    return span / (this.ts.length - 1);
  }
}

export function latencyMs(emitUnixMs: number, nowMs: number): number {
  return nowMs - emitUnixMs;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/hud/fps.ts rust/src/hud/fps.test.ts
git commit -m "feat(rust-fe): FPS meter + latency math"
```

---

### Task 7: Gauge geometry

**Files:**
- Create: `rust/src/gauge/geometry.ts`
- Test: `rust/src/gauge/geometry.test.ts`

**Interfaces:**
- Produces:
  - `gaugeAngle(value: number, min: number, max: number, startDeg: number, endDeg: number): number` — clamps `value` to `[min,max]`, linearly maps to `[startDeg,endDeg]`

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/gauge/geometry.test.ts
import { describe, it, expect } from "vitest";
import { gaugeAngle } from "./geometry";

describe("gaugeAngle", () => {
  it("maps min/mid/max across the sweep", () => {
    expect(gaugeAngle(-180, -180, 180, -120, 120)).toBe(-120);
    expect(gaugeAngle(0, -180, 180, -120, 120)).toBe(0);
    expect(gaugeAngle(180, -180, 180, -120, 120)).toBe(120);
  });

  it("clamps out-of-range values", () => {
    expect(gaugeAngle(999, 0, 10, 0, 90)).toBe(90);
    expect(gaugeAngle(-5, 0, 10, 0, 90)).toBe(0);
  });

  it("handles degenerate min==max", () => {
    expect(gaugeAngle(5, 5, 5, 0, 90)).toBe(0);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./geometry`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/gauge/geometry.ts
export function gaugeAngle(value: number, min: number, max: number, startDeg: number, endDeg: number): number {
  if (max <= min) return startDeg;
  const clamped = Math.min(max, Math.max(min, value));
  const t = (clamped - min) / (max - min);
  return startDeg + t * (endDeg - startDeg);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/gauge/geometry.ts rust/src/gauge/geometry.test.ts
git commit -m "feat(rust-fe): gauge needle geometry"
```

---

### Task 8: Telemetry store (integration)

**Files:**
- Create: `rust/src/data/store.ts`
- Test: `rust/src/data/store.test.ts`

**Interfaces:**
- Consumes: `ChannelMeta`, `MetaMessage`, `FrameMessage`, `MetricsMessage`, `ChannelSeries`, `buildEnumIndex`
- Produces:
  - `class TelemetryStore`:
    - `applyMeta(m: MetaMessage): void` — stores channels (already display-ordered), builds the enum index, creates a `ChannelSeries` (default window 60_000 ms) for each `widget === "strip"` channel, and tracks the indices of `map_lat`/`map_lon` channels
    - `applyFrame(f: FrameMessage): void` — records latest values + `emit_unix_ms`; pushes `(ts_ms, value)` into each strip channel's series and into the GPS track
    - `applyMetrics(m: MetricsMessage): void`
    - `latest(channelId: number): number | undefined`
    - `series(channelId: number): ChannelSeries | undefined`
    - `gpsTrack(): { lat: number[]; lon: number[] }`
    - getters: `channels(): ChannelMeta[]`, `enumIndex(): Map<string, EnumValue>`, `lastEmitUnixMs(): number`, `metrics(): MetricsMessage | undefined`
    - constructor: `constructor(windowMs = 60_000)`

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/data/store.test.ts
import { describe, it, expect } from "vitest";
import { TelemetryStore } from "./store";
import type { MetaMessage, FrameMessage } from "../types";

function meta(): MetaMessage {
  return {
    type: "meta",
    rate_hz: 10,
    enum_values: [{ channel_id: 3, code: 1, label: "Critical", severity: "critical" }],
    channels: [
      { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" },
      { id: 2, name: "Lat", column_name: "lat", unit: "deg", type: "real", min: 31, max: 33, widget: "map_lat", display_order: 2, addr: "I_09" },
      { id: 3, name: "Lon", column_name: "lon", unit: "deg", type: "real", min: 34, max: 35, widget: "map_lon", display_order: 3, addr: "I_09" },
    ],
  };
}

describe("TelemetryStore", () => {
  it("creates series only for strip channels and records latest values", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    const f: FrameMessage = { type: "frame", ts_ms: 100, emit_unix_ms: 1700000000000, values: [12.5, 32.0, 34.5] };
    s.applyFrame(f);
    expect(s.latest(1)).toBe(12.5);
    expect(s.series(1)?.len()).toBe(1);   // strip channel buffered
    expect(s.series(2)).toBeUndefined();   // map channel has no strip series
    expect(s.lastEmitUnixMs()).toBe(1700000000000);
  });

  it("accumulates a GPS track from map_lat/map_lon channels", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [0, 32.0, 34.5] });
    s.applyFrame({ type: "frame", ts_ms: 100, emit_unix_ms: 2, values: [0, 32.1, 34.6] });
    expect(s.gpsTrack().lat).toEqual([32.0, 32.1]);
    expect(s.gpsTrack().lon).toEqual([34.5, 34.6]);
  });

  it("stores metrics", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyMetrics({ type: "metrics", cpu_pct: 5, ram_mb: 90 });
    expect(s.metrics()?.ram_mb).toBe(90);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./store`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/data/store.ts
import type { ChannelMeta, EnumValue, MetaMessage, FrameMessage, MetricsMessage } from "../types";
import { ChannelSeries } from "./ringBuffer";
import { buildEnumIndex } from "../format/value";

export class TelemetryStore {
  private _channels: ChannelMeta[] = [];
  private _enumIndex = new Map<string, EnumValue>();
  private _latest: number[] = [];
  private _series = new Map<number, ChannelSeries>();
  private _latIdx = -1;
  private _lonIdx = -1;
  private _lat: number[] = [];
  private _lon: number[] = [];
  private _lastEmit = 0;
  private _metrics: MetricsMessage | undefined;

  constructor(private readonly windowMs = 60_000) {}

  applyMeta(m: MetaMessage): void {
    this._channels = m.channels;
    this._enumIndex = buildEnumIndex(m.enum_values);
    this._series.clear();
    this._lat = [];
    this._lon = [];
    this._latIdx = -1;
    this._lonIdx = -1;
    m.channels.forEach((ch, i) => {
      if (ch.widget === "strip") this._series.set(ch.id, new ChannelSeries(this.windowMs));
      if (ch.widget === "map_lat") this._latIdx = i;
      if (ch.widget === "map_lon") this._lonIdx = i;
    });
  }

  applyFrame(f: FrameMessage): void {
    this._latest = f.values;
    this._lastEmit = f.emit_unix_ms;
    this._channels.forEach((ch, i) => {
      const series = this._series.get(ch.id);
      if (series) series.push(f.ts_ms, f.values[i]);
    });
    if (this._latIdx >= 0) this._lat.push(f.values[this._latIdx]);
    if (this._lonIdx >= 0) this._lon.push(f.values[this._lonIdx]);
  }

  applyMetrics(m: MetricsMessage): void {
    this._metrics = m;
  }

  latest(channelId: number): number | undefined {
    const i = this._channels.findIndex((c) => c.id === channelId);
    return i >= 0 ? this._latest[i] : undefined;
  }

  series(channelId: number): ChannelSeries | undefined {
    return this._series.get(channelId);
  }

  gpsTrack(): { lat: number[]; lon: number[] } {
    return { lat: this._lat, lon: this._lon };
  }

  channels(): ChannelMeta[] {
    return this._channels;
  }

  enumIndex(): Map<string, EnumValue> {
    return this._enumIndex;
  }

  lastEmitUnixMs(): number {
    return this._lastEmit;
  }

  metrics(): MetricsMessage | undefined {
    return this._metrics;
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `rust/`): `npm test`
Expected: PASS — all suites (decode, client, ringBuffer, value, fps, geometry, store).

- [ ] **Step 5: Commit**

```bash
git add rust/src/data/store.ts rust/src/data/store.test.ts
git commit -m "feat(rust-fe): telemetry store integrating series, enum, gps track"
```

---

## Self-Review

**Spec coverage:**
- WS protocol decode (meta/frame/metrics) → Task 2 ✓
- WS client + reconnect → Task 3 ✓
- Strip-chart windowed buffering (60s default) → Tasks 4, 8 ✓
- `values` index-aligned to channels (display order) → Task 8 (`forEach((ch,i)=>... f.values[i])`) ✓
- Enum decode + severity color + value formatting → Task 5 ✓
- HUD: FPS/frame time + latency (`Date.now()-emit_unix_ms`) → Task 6 ✓
- Gauge needle math → Task 7 ✓
- GPS track from map_lat/map_lon channels → Task 8 ✓
- Telemetry state aggregation → Task 8 ✓
- TS strict, pure logic, vitest → Task 1 + all ✓

**Placeholder scan:** No TBD/TODO; every step has real code + commands. ✓

**Type consistency:** `WsMessage` union + `decodeMessage` (Task 2) consumed by client (Task 3) and store (Task 8). `ChannelSeries` (Task 4) used by store (Task 8). `buildEnumIndex` (Task 5) used by store (Task 8). `MetaMessage/FrameMessage/MetricsMessage` shared across 2,3,8. `latest()` resolves channel id → index consistently with how `applyFrame` aligns `values[i]` to `channels[i]`. ✓

> **Note for Plan 5 (UI):** components import from `src/data/store` (`TelemetryStore`), `src/ws/client` (`createWsClient`), `src/format/value` (`formatValue`/`severityColor`), `src/hud/fps` (`FpsMeter`/`latencyMs`), `src/gauge/geometry` (`gaugeAngle`). Add uPlot/Leaflet there. Strip charts read `store.series(id).arrays()`; map reads `store.gpsTrack()`; HUD reads `latencyMs(store.lastEmitUnixMs(), Date.now())` + `store.metrics()`.
