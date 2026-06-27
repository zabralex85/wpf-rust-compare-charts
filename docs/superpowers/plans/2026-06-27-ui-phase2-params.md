# INU-MONITOR Rewrite — Phase 2: Ride-time + Parameters Panel

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (a) Wire the shell's transport to real ride playback — the top-bar clock shows mission time, `T+` counts up, and the scrubber reflects ride progress; (b) replace the OVERVIEW body stub with the grouped, live PARAMETERS panel from the INU-MONITOR design.

**Architecture:** A small backend addition sends ride `duration_s` in the `meta` message; the store gains a latest-ride-`ts` getter + rate/duration accessors; the shell formats mission time / progress from those. The Parameters panel is built from a static channel→group table and per-row value/severity logic (both vitest-tested), rendered by a `ParamPanel` component (jsdom smoke-tested) that replaces the `OverviewView` stub's left column.

**Tech Stack:** Rust (backend meta), React 18 + TS strict, vitest, the existing data layer + `useTelemetry`.

## Global Constraints

- Design source: `docs/sample ui/INU Monitor (standalone-src).html` (PARAM PANEL section) + the design spec. Exact INU palette via the theme vars from Phase 1.
- Reuse Phase-1 shell (`rust/src/ui/app/*`) and the data layer. The store may gain **additive** getters + track latest ride `ts` (no behavior change to existing methods); the backend `meta` gains a `duration_s` field.
- Keep the two-stack wire contract documented: `meta` now also carries `duration_s` (the .NET app ignores it harmlessly; the Rust app uses it).
- TS strict, no `any`; vitest node/jsdom split intact (component tests `// @vitest-environment jsdom` + local `afterEach(cleanup)`); React 19 type-only React imports; CSS uses theme vars (color-mix for translucency).

---

### Task 1: Backend — ride `duration_s` in the meta message

**Files:**
- Modify: `rust/src-tauri/src/frame.rs` (`MetaMessage` + constructor)
- Modify: `rust/src-tauri/src/server.rs` (load + pass `duration_s`)
- Test: extend `rust/src-tauri/tests/ws_integration.rs`

**Interfaces:**
- Produces: `MetaMessage` gains `pub duration_s: i64`; `MetaMessage::new(channels, enum_values, rate_hz, duration_s)`. The server reads `duration_s` from `ride_meta` and passes it.

- [ ] **Step 1: Extend the integration test (failing)**

In `rust/src-tauri/tests/ws_integration.rs`, in `client_receives_meta_then_frames`, after asserting `v["type"] == "meta"`, add:
```rust
    assert!(v["rate_hz"].as_i64().unwrap() >= 1);
    assert!(v["duration_s"].as_i64().unwrap() >= 1); // ride length present
```

Run (from `rust/src-tauri`): `cargo test --test ws_integration`
Expected: FAIL — `duration_s` not present in the meta JSON.

- [ ] **Step 2: Add the field + constructor**

In `frame.rs`, add `pub duration_s: i64` to `MetaMessage` and the constructor param:
```rust
impl MetaMessage {
    pub fn new(channels: Vec<ChannelMeta>, enum_values: Vec<EnumValue>, rate_hz: i64, duration_s: i64) -> Self {
        Self { type_: "meta", channels, enum_values, rate_hz, duration_s }
    }
}
```
(Add the `duration_s` field to the struct definition above the constructor.)

- [ ] **Step 3: Load + pass it in the server**

In `server.rs`'s `handle_client` blocking load closure, alongside the `rate` query add:
```rust
        let duration_s: i64 = conn.query_row("SELECT duration_s FROM ride_meta", [], |r| r.get(0))?;
```
and build the meta with it: `let meta = MetaMessage::new(channels, enums, rate, duration_s);`. Return `duration_s` is not needed outside; keep it local.

- [ ] **Step 4: Verify**

Run (from `rust/src-tauri`): `cargo test`
Expected: all pass (the integration test now sees `duration_s`).

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/frame.rs rust/src-tauri/src/server.rs rust/src-tauri/tests/ws_integration.rs
git commit -m "feat(rust-ui): send ride duration_s in the meta message"
```

---

### Task 2: Frontend store — latest ride ts + rate/duration

**Files:**
- Modify: `rust/src/types.ts` (`MetaMessage.duration_s`)
- Modify: `rust/src/data/store.ts` (track `lastTsMs`; expose `lastTsMs()`, `rateHz()`, `durationMs()`)
- Test: `rust/src/data/store.test.ts` (extend)

**Interfaces:**
- Produces: `MetaMessage` gains `duration_s: number`. `TelemetryStore` gains:
  - `lastTsMs(): number` — `ts_ms` of the most recent applied frame (0 before any)
  - `rateHz(): number` — from meta (0 before meta)
  - `durationMs(): number` — `duration_s * 1000` from meta (0 before meta)
  Existing methods unchanged; `applyMeta` records rate/duration, `applyFrame` records `f.ts_ms`; `applyMeta` resets `lastTsMs` to 0.

- [ ] **Step 1: Write the failing test** (extend `store.test.ts`)

```ts
  it("tracks last ride ts, rate, and duration", () => {
    const s = new TelemetryStore();
    s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 600, enum_values: [],
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "strip", display_order: 1, addr: "I_01" }] });
    expect(s.rateHz()).toBe(10);
    expect(s.durationMs()).toBe(600_000);
    expect(s.lastTsMs()).toBe(0);
    s.applyFrame({ type: "frame", ts_ms: 4200, emit_unix_ms: 1, values: [1] });
    expect(s.lastTsMs()).toBe(4200);
  });
```
(Update the existing `meta()` helper in this test file to include `duration_s` so it stays type-valid.)

Run (from `rust/`): `npm test` → FAIL (methods/field missing).

- [ ] **Step 2: Implement**

In `types.ts` add `duration_s: number;` to `MetaMessage`.
In `store.ts`: add private `_rateHz = 0`, `_durationMs = 0`, `_lastTs = 0`. In `applyMeta`: `this._rateHz = m.rate_hz; this._durationMs = m.duration_s * 1000; this._lastTs = 0;` (alongside the existing resets). In `applyFrame` (after the length guard): `this._lastTs = f.ts_ms;`. Add getters:
```ts
  lastTsMs(): number { return this._lastTs; }
  rateHz(): number { return this._rateHz; }
  durationMs(): number { return this._durationMs; }
```

- [ ] **Step 3: Verify** — `npm test` (all green) + `npx tsc --noEmit`. NOTE: any other test/code constructing a `MetaMessage` literal must now include `duration_s` — update those (e.g. shell `status.test.ts`, AppShell, the Phase-1 `meta()` helpers) to add `duration_s: <n>` so tsc passes. Grep `type: "meta"` and fix each literal.

- [ ] **Step 4: Commit**

```bash
git add rust/src/types.ts rust/src/data/store.ts rust/src/data/store.test.ts
git commit -m "feat(rust-ui): store tracks last ride ts + rate + duration"
```

---

### Task 3: Ride-time formatting + transport wiring

**Files:**
- Create: `rust/src/ui/app/ridetime.ts`
- Test: `rust/src/ui/app/ridetime.test.ts`
- Modify: `rust/src/ui/app/AppShell.tsx`

**Interfaces:**
- Produces:
  - `rideClock(tsMs: number): { hms: string; ms: string }` — ride time as `HH:MM:SS` + millis (HH can exceed 24 for long rides; from ms-since-start)
  - `rideProgress(tsMs: number, durationMs: number): number` — `clamp(tsMs / durationMs, 0..1)`, `0` if `durationMs <= 0`
- AppShell change: clock = `rideClock(store.lastTsMs())` (fall back to wall clock only if no data — keep it simple: use `rideClock`); `rideTag = formatRideTag(store.lastTsMs())`; `samples`/`elapsedMs` from `store.lastTsMs()`; `scrubberFrac = rideProgress(store.lastTsMs(), store.durationMs())`; `rateHz = store.rateHz() || 10`.

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/app/ridetime.test.ts
import { describe, it, expect } from "vitest";
import { rideClock, rideProgress } from "./ridetime";

describe("rideClock", () => {
  it("formats ride ms as HH:MM:SS + millis", () => {
    const c = rideClock(11 * 3600_000 + 33 * 60_000 + 26_000 + 878);
    expect(c.hms).toBe("11:33:26");
    expect(c.ms).toBe("878");
  });
  it("allows >24h", () => {
    expect(rideClock(30 * 3600_000).hms).toBe("30:00:00");
  });
});

describe("rideProgress", () => {
  it("is ts over duration, clamped", () => {
    expect(rideProgress(300_000, 600_000)).toBeCloseTo(0.5);
    expect(rideProgress(900_000, 600_000)).toBe(1);
    expect(rideProgress(100, 0)).toBe(0);
  });
});
```

Run: `npm test` → FAIL.

- [ ] **Step 2: Implement**

```ts
// rust/src/ui/app/ridetime.ts
function p2(n: number): string { return n.toString().padStart(2, "0"); }
function p3(n: number): string { return n.toString().padStart(3, "0"); }

export function rideClock(tsMs: number): { hms: string; ms: string } {
  const t = Math.max(0, Math.floor(tsMs));
  const totalSec = Math.floor(t / 1000);
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  return { hms: `${p2(h)}:${p2(m)}:${p2(s)}`, ms: p3(t % 1000) };
}

export function rideProgress(tsMs: number, durationMs: number): number {
  if (durationMs <= 0) return 0;
  return Math.min(1, Math.max(0, tsMs / durationMs));
}
```

- [ ] **Step 3: Wire AppShell**

In `AppShell.tsx`, replace the placeholder derivations:
```tsx
  const tsMs = store.lastTsMs();
  const clock = rideClock(tsMs);
  const rideTag = formatRideTag(tsMs);
  const rateHz = store.rateHz() || 10;
  const samples = channels.reduce((m, c) => Math.max(m, store.series(c.id)?.len() ?? 0), 0);
  const scrubberFrac = rideProgress(tsMs, store.durationMs());
```
and pass `elapsedMs={tsMs}` and `scrubberFrac={scrubberFrac}` to `TransportBar`. Import `rideClock`/`rideProgress` from `./ridetime`; remove the now-unused `formatClock`/`Date.now()` import for the clock (keep `formatRideTag`). The `AppShell.test.tsx` still passes (it only checks tab switching). Update its `meta`-shaped data only if it constructs one.

- [ ] **Step 4: Verify** — `npm test` + `npx tsc --noEmit` + `npm run build`.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/ridetime.ts rust/src/ui/app/ridetime.test.ts rust/src/ui/app/AppShell.tsx
git commit -m "feat(rust-ui): clock=mission time, T+ counts, scrubber=ride progress"
```

---

### Task 4: Channel grouping

**Files:**
- Create: `rust/src/ui/app/groups.ts`
- Test: `rust/src/ui/app/groups.test.ts`

**Interfaces:**
- Consumes: `ChannelMeta`
- Produces:
  - `GROUP_OF: Record<string, string>` — column_name → group label (see mapping below)
  - `groupOf(columnName: string): string` — returns the mapped group or `"System"`
  - `groupChannels(channels: ChannelMeta[]): { group: string; channels: ChannelMeta[] }[]` — channels bucketed into groups, groups in this order: `INU Mode, Velocity, Attitude, Acceleration, Body Rates, Position, System`; empty groups omitted; channel order within a group follows `display_order`.

Mapping (column_name → group):
- INU Mode: `inu_mode1`, `inu_mode2`
- Velocity: `vel_x`, `vel_y`, `vel_z`, `plat_azim`, `vclimb`
- Attitude: `roll`, `pitch`, `heading_t`, `heading_m`, `sky_pitch`, `sky_roll`, `sky_azim`, `sky_heading`, `prsnt_head`
- Acceleration: `acc_x`, `acc_y`, `acc_z`
- Body Rates: `roll_r`, `pitch_r`, `yaw_r`
- Position: `lat`, `lon`
- System: everything else (`alt_i`, `gcs_err`, `vtime_tag`, `gcs_range`, `temp`, `voltage`, …)

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/app/groups.test.ts
import { describe, it, expect } from "vitest";
import { groupOf, groupChannels } from "./groups";
import type { ChannelMeta } from "../../types";

function ch(col: string, order: number): ChannelMeta {
  return { id: order, name: col, column_name: col, unit: "", type: "real", min: 0, max: 1, widget: "table", display_order: order, addr: "I_01" };
}

describe("groupOf", () => {
  it("maps known columns and defaults to System", () => {
    expect(groupOf("roll")).toBe("Attitude");
    expect(groupOf("vel_x")).toBe("Velocity");
    expect(groupOf("inu_mode2")).toBe("INU Mode");
    expect(groupOf("temp")).toBe("System");
    expect(groupOf("wat")).toBe("System");
  });
});

describe("groupChannels", () => {
  it("buckets in group order, omits empties, sorts by display_order", () => {
    const chs = [ch("temp", 3), ch("roll", 2), ch("pitch", 1), ch("vel_x", 4)];
    const g = groupChannels(chs);
    expect(g.map((x) => x.group)).toEqual(["Velocity", "Attitude", "System"]);
    const att = g.find((x) => x.group === "Attitude")!;
    expect(att.channels.map((c) => c.column_name)).toEqual(["pitch", "roll"]);
  });
});
```

Run: `npm test` → FAIL.

- [ ] **Step 2: Implement**

```ts
// rust/src/ui/app/groups.ts
import type { ChannelMeta } from "../../types";

const GROUPS: { group: string; cols: string[] }[] = [
  { group: "INU Mode", cols: ["inu_mode1", "inu_mode2"] },
  { group: "Velocity", cols: ["vel_x", "vel_y", "vel_z", "plat_azim", "vclimb"] },
  { group: "Attitude", cols: ["roll", "pitch", "heading_t", "heading_m", "sky_pitch", "sky_roll", "sky_azim", "sky_heading", "prsnt_head"] },
  { group: "Acceleration", cols: ["acc_x", "acc_y", "acc_z"] },
  { group: "Body Rates", cols: ["roll_r", "pitch_r", "yaw_r"] },
  { group: "Position", cols: ["lat", "lon"] },
];

export const GROUP_OF: Record<string, string> = {};
for (const g of GROUPS) for (const c of g.cols) GROUP_OF[c] = g.group;

const ORDER = [...GROUPS.map((g) => g.group), "System"];

export function groupOf(columnName: string): string {
  return GROUP_OF[columnName] ?? "System";
}

export function groupChannels(channels: ChannelMeta[]): { group: string; channels: ChannelMeta[] }[] {
  const buckets = new Map<string, ChannelMeta[]>();
  for (const ch of channels) {
    const g = groupOf(ch.column_name);
    (buckets.get(g) ?? buckets.set(g, []).get(g)!).push(ch);
  }
  return ORDER.flatMap((group) => {
    const list = buckets.get(group);
    if (!list || list.length === 0) return [];
    list.sort((a, b) => a.display_order - b.display_order);
    return [{ group, channels: list }];
  });
}
```

- [ ] **Step 3: Verify** — `npm test` + tsc.

- [ ] **Step 4: Commit**

```bash
git add rust/src/ui/app/groups.ts rust/src/ui/app/groups.test.ts
git commit -m "feat(rust-ui): channel grouping for the parameters panel"
```

---

### Task 5: Param row presentation logic

**Files:**
- Create: `rust/src/ui/app/paramrow.ts`
- Test: `rust/src/ui/app/paramrow.test.ts`

**Interfaces:**
- Consumes: `ChannelMeta`, `TelemetryStore`, `formatValue`, `decodeEnum`, `severityColor`
- Produces: `paramRow(ch: ChannelMeta, store: TelemetryStore): { text: string; valueColor: string; dotColor: string; critical: boolean }`
  - `text` = `formatValue(ch, latest, enumIndex)` (or `"—"` if no latest)
  - severity (for enum: `decodeEnum(...).severity`; for real: `"critical"` if value ≤ min or ≥ max, else `"ok"`)
  - `dotColor` = `severityColor(severity)`; `valueColor` = enum→severityColor, real-ok→`var(--green2)` for in-band else the severity color; default text `var(--text)`. Keep it simple + deterministic (see code).
  - `critical` = severity === "critical"

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/app/paramrow.test.ts
import { describe, it, expect } from "vitest";
import { paramRow } from "./paramrow";
import { TelemetryStore } from "../../data/store";
import type { ChannelMeta } from "../../types";

function store(): TelemetryStore {
  const s = new TelemetryStore();
  s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60,
    enum_values: [{ channel_id: 2, code: 1, label: "Critical", severity: "critical" }],
    channels: [
      { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" },
      { id: 2, name: "Mode", column_name: "inu_mode2", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 2, addr: "I_01" },
    ] });
  s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [90, 1] });
  return s;
}

describe("paramRow", () => {
  it("formats a real channel value, not critical in-band", () => {
    const s = store();
    const r = paramRow(s.channels()[0], s);
    expect(r.text).toBe("90.000");
    expect(r.critical).toBe(false);
  });
  it("marks an enum critical channel", () => {
    const s = store();
    const r = paramRow(s.channels()[1], s);
    expect(r.text).toBe("Critical");
    expect(r.critical).toBe(true);
    expect(r.dotColor).toBe("#d22"); // severityColor("critical")
  });
  it("shows dash when no latest", () => {
    const s = new TelemetryStore();
    s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60, enum_values: [],
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" }] });
    expect(paramRow(s.channels()[0], s).text).toBe("—");
  });
});
```

Run: `npm test` → FAIL.

- [ ] **Step 2: Implement**

```ts
// rust/src/ui/app/paramrow.ts
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
```

- [ ] **Step 3: Verify** — `npm test` + tsc.

- [ ] **Step 4: Commit**

```bash
git add rust/src/ui/app/paramrow.ts rust/src/ui/app/paramrow.test.ts
git commit -m "feat(rust-ui): param row value + severity presentation logic"
```

---

### Task 6: ParamPanel component

**Files:**
- Create: `rust/src/ui/app/ParamPanel.tsx`
- Test: `rust/src/ui/app/ParamPanel.test.tsx`
- Modify: `rust/src/ui/app/theme.css` (param panel styles)

**Interfaces:**
- Consumes: `TelemetryStore`, `groupChannels`, `paramRow`
- Produces: `ParamPanel({ store }: { store: TelemetryStore }): React.JSX.Element` — panel header ("PARAMETERS" + `N CH` + `ALL`), a column header row (`ADDR | PARAMETER | ENG·DATA | BUS`), then per group: a group header (label + count) and the channel rows (status dot, addr, name, value (colored), unit, bus(addr)); critical rows get the `param-row-crit` class. Matches the sample's grid columns.

- [ ] **Step 1: Write the failing test**

```tsx
// rust/src/ui/app/ParamPanel.test.tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { ParamPanel } from "./ParamPanel";
import { TelemetryStore } from "../../data/store";

afterEach(() => cleanup());

function store(): TelemetryStore {
  const s = new TelemetryStore();
  s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60,
    enum_values: [{ channel_id: 2, code: 1, label: "Critical", severity: "critical" }],
    channels: [
      { id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" },
      { id: 2, name: "Mode", column_name: "inu_mode2", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 2, addr: "I_01" },
    ] });
  s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [90, 1] });
  return s;
}

describe("ParamPanel", () => {
  it("renders group headers, rows, value, and the CH count", () => {
    const { container } = render(<ParamPanel store={store()} />);
    const t = container.textContent ?? "";
    expect(t).toContain("PARAMETERS");
    expect(t).toContain("2 CH");
    expect(t).toContain("Attitude");
    expect(t).toContain("INU Mode");
    expect(t).toContain("Roll");
    expect(t).toContain("90.000");
    expect(t).toContain("Critical");
  });

  it("marks the critical row", () => {
    const { container } = render(<ParamPanel store={store()} />);
    expect(container.querySelector(".param-row-crit")).not.toBeNull();
  });
});
```

Run: `npm test` → FAIL.

- [ ] **Step 2: Implement** (component + CSS)

```tsx
// rust/src/ui/app/ParamPanel.tsx
import type React from "react";
import type { TelemetryStore } from "../../data/store";
import { groupChannels } from "./groups";
import { paramRow } from "./paramrow";

export function ParamPanel({ store }: { store: TelemetryStore }): React.JSX.Element {
  const groups = groupChannels(store.channels());
  return (
    <div className="param-panel">
      <div className="param-hd">
        <div className="param-hd-title"><span className="param-hd-dot" />PARAMETERS</div>
        <div className="param-hd-meta"><span className="param-all">ALL</span><span className="param-ch">{store.channels().length} CH</span></div>
      </div>
      <div className="param-colhd"><span /><span>ADDR</span><span>PARAMETER</span><span className="r">ENG·DATA</span><span /><span className="r">BUS</span></div>
      <div className="param-rows">
        {groups.map((g) => (
          <div key={g.group}>
            <div className="param-grp"><span>{g.group}</span><span>{g.channels.length}</span></div>
            {g.channels.map((ch) => {
              const r = paramRow(ch, store);
              return (
                <div key={ch.id} className={`param-row${r.critical ? " param-row-crit" : ""}`}>
                  <span className="param-dot" style={{ background: r.dotColor }} />
                  <span className="param-addr">{ch.addr}</span>
                  <span className="param-name">{ch.name}</span>
                  <span className="param-val r" style={{ color: r.valueColor }}>{r.text}</span>
                  <span className="param-unit">{ch.unit}</span>
                  <span className="param-bus r">{ch.addr}</span>
                </div>
              );
            })}
          </div>
        ))}
      </div>
    </div>
  );
}
```

Add `.param-panel`/`.param-hd*`/`.param-colhd`/`.param-grp`/`.param-row*`/`.param-dot`/`.param-val`/`.r` styles to `theme.css` per the sample (grid columns `9px 52px 1fr 90px 22px 34px`, mono font, dark panel, critical row bg `var(--critbg)`, dim group headers). Use theme vars.

- [ ] **Step 3: Verify** — `npm test` (2 ParamPanel tests + all) + tsc + build.

- [ ] **Step 4: Commit**

```bash
git add rust/src/ui/app/ParamPanel.tsx rust/src/ui/app/ParamPanel.test.tsx rust/src/ui/app/theme.css
git commit -m "feat(rust-ui): grouped live PARAMETERS panel"
```

---

### Task 7: OverviewView — param panel + dashboard placeholder

**Files:**
- Modify: `rust/src/ui/app/views/OverviewView.tsx`
- Test: `rust/src/ui/app/views/OverviewView.test.tsx`
- Modify: `rust/src/ui/app/theme.css` (overview two-column layout)

**Interfaces:**
- Produces: `OverviewView({ store })` renders a two-column layout — left = `ParamPanel`, right = a placeholder dashboard area (a panel reading "DASHBOARD — widgets in Phase 3", `data-testid="overview-dash"`). Keeps `data-testid="view-overview"` on the root so the AppShell test still passes.

- [ ] **Step 1: Write the failing test**

```tsx
// rust/src/ui/app/views/OverviewView.test.tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { OverviewView } from "./OverviewView";
import { TelemetryStore } from "../../../data/store";

afterEach(() => cleanup());

describe("OverviewView", () => {
  it("renders the param panel + a dashboard placeholder", () => {
    const s = new TelemetryStore();
    s.applyMeta({ type: "meta", rate_hz: 10, duration_s: 60, enum_values: [],
      channels: [{ id: 1, name: "Roll", column_name: "roll", unit: "deg", type: "real", min: -180, max: 180, widget: "table", display_order: 1, addr: "I_01" }] });
    const { container } = render(<OverviewView store={s} />);
    expect(container.querySelector('[data-testid="view-overview"]')).not.toBeNull();
    expect(container.textContent).toContain("PARAMETERS");
    expect(container.querySelector('[data-testid="overview-dash"]')).not.toBeNull();
  });
});
```

Run: `npm test` → FAIL (current stub has no PARAMETERS).

- [ ] **Step 2: Implement**

```tsx
// rust/src/ui/app/views/OverviewView.tsx
import type React from "react";
import type { TelemetryStore } from "../../../data/store";
import { ParamPanel } from "../ParamPanel";

export function OverviewView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return (
    <div data-testid="view-overview" className="overview">
      <div className="overview-left"><ParamPanel store={store} /></div>
      <div data-testid="overview-dash" className="overview-dash">DASHBOARD — widgets in Phase 3</div>
    </div>
  );
}
```
Add `.overview{display:grid;grid-template-columns:474px 1fr;gap:10px;height:100%;padding:10px}` + `.overview-left`/`.overview-dash` styles to `theme.css`.

- [ ] **Step 3: Verify** — `npm test` (all, incl AppShell tab test still green) + tsc + build.

- [ ] **Step 4: Commit**

```bash
git add rust/src/ui/app/views/OverviewView.tsx rust/src/ui/app/views/OverviewView.test.tsx rust/src/ui/app/theme.css
git commit -m "feat(rust-ui): overview = param panel + dashboard placeholder"
```

---

### Task 8: Build + live verify

Manual. No new unit tests.

- [ ] **Step 1: Full check** — from `rust/`: `npx tsc --noEmit && npm test && npm run build`; from `rust/src-tauri`: `cargo test`. All green.
- [ ] **Step 2: Launch** — `RIDE_DB=../../data/ride_small.db RIDE_SPEED=5 npm run tauri dev`. Confirm: the clock now shows **ride/mission time** counting, `T+` counting up, the scrubber advancing across the ride; the OVERVIEW body now shows the **grouped PARAMETERS panel** on the left (INU Mode / Velocity / Attitude / Acceleration / Body Rates / Position / System) with live values, green/amber/red status dots, and the critical row (inu_mode2 → CRITICAL) highlighted. Compare to `docs/sample ui/screenshots/overview-full.png` (left panel + top bar/transport).
- [ ] **Step 3: Commit any visual tweaks** — `git commit -am "feat(rust-ui): align param panel visuals with the reference"`.

---

## Self-Review

**Spec coverage:** ride-time wiring (clock=mission time, T+ count, scrubber=progress) → Tasks 1-3; grouped live PARAMETERS panel → Tasks 4-7. ✓
**Placeholder scan:** No TBD; the right dashboard column is an explicit Phase-3 placeholder (Task 7). `rateHz` now sourced from the store (not the old constant). ✓
**Type consistency:** `MetaMessage.duration_s` (Tasks 1,2) flows to `store.durationMs()` (2) → `rideProgress` (3). `groupChannels` (4) + `paramRow` (5) consumed by `ParamPanel` (6) → `OverviewView` (7). Backend `MetaMessage::new` arity change (Task 1) is matched in `server.rs`. The Task-2 note flags updating every `type:"meta"` literal in tests to include `duration_s`. ✓

> **Note for Phase 3:** `overview-dash` is the mount point for the widget grid (gauges/line/map). The store now exposes `rateHz()`/`durationMs()`/`lastTsMs()` for the transport and any time-axis needs.
