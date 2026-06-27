# INU-MONITOR Rewrite — Phase 1: Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the app shell of the INU-MONITOR rewrite — dark theme + IBM Plex fonts, the top bar (logo/id, tab nav, alarm·caution·link, clock, scales toggle), tab routing (Overview/Track/Events stubs), and the bottom transport timeline — wired to the live telemetry store.

**Architecture:** New `rust/src/ui/app/` tree. Pure logic (clock formatting, status derivation, tab styling, transport labels) is vitest-tested; React components (`TopBar`, `TransportBar`, `AppShell`) get jsdom smoke tests; visual correctness is confirmed by launching the app. Reuses the existing `useTelemetry` hook and the tested data layer. Tab content is stubs to be filled in Phases 2-6.

**Tech Stack:** React 18 + TypeScript (strict), Vite, vitest (node + jsdom per-file), `@fontsource/ibm-plex-sans`, `@fontsource/ibm-plex-mono`.

## Global Constraints

- Design source of truth: `docs/sample ui/INU Monitor (standalone-src).html` + `docs/superpowers/specs/2026-06-27-inu-monitor-rewrite-design.md`. Use the exact colors/fonts from the spec.
- Reuse `src/ui/useTelemetry.ts` (`useTelemetry(url) -> {store, fps, frameTimeMs, status, version}`) and the data layer (`store.channels/latest/enumIndex/lastEmitUnixMs/metrics`). Do NOT modify the data layer.
- TypeScript strict; no `any` in exported signatures.
- vitest split intact: node default, component tests use `// @vitest-environment jsdom` (first line). Do NOT edit `vitest.config.ts` or add a global setup file; if a component test renders twice, add a LOCAL `afterEach(cleanup)` in that file.
- React 19 `useRef` needs an initial value; type-only `import type React` where a JSX return type is needed.
- Theme via a single `src/ui/app/theme.css` imported once; fonts via `@fontsource` imports.

---

### Task 1: Fonts + theme CSS

**Files:**
- Modify: `rust/package.json` (add `@fontsource/ibm-plex-sans`, `@fontsource/ibm-plex-mono`)
- Create: `rust/src/ui/app/theme.css`

**Interfaces:**
- Produces: `theme.css` defining the INU palette as CSS variables on `:root`, base `body`/scrollbar styles, and the `blink` keyframe; importable once from the shell.

This is styling + a dependency; verification is build, not a unit test.

- [ ] **Step 1: Install the fonts**

Run (from `rust/`): `npm install @fontsource/ibm-plex-sans @fontsource/ibm-plex-mono`

- [ ] **Step 2: Write theme.css**

```css
/* rust/src/ui/app/theme.css */
:root{
  --bg:#0a0e14; --panel:#10151d; --panel2:#0c121a; --panelhdr:#131a24;
  --border:#1d2632; --border2:#1a2230; --colhdr:#0c1119;
  --text:#cdd6e1; --text2:#c3ccd8; --dim:#566273; --dim2:#5d6b7c; --addr:#4f5a68;
  --title:#8b98a9; --accent:#38c5e0; --green:#2fd17a; --green2:#38d178;
  --amber:#f5b440; --amber2:#f5c061; --red:#ff4d52; --red2:#ff6b70; --critbg:#1a0e11;
  --sans:'IBM Plex Sans',system-ui,sans-serif; --mono:'IBM Plex Mono',monospace;
}
*{box-sizing:border-box}
html,body,#root{height:100%;margin:0}
body{background:var(--bg);color:var(--text);font-family:var(--sans);overflow:hidden}
::-webkit-scrollbar{width:9px;height:9px}
::-webkit-scrollbar-track{background:#0b1018}
::-webkit-scrollbar-thumb{background:#222d3c;border-radius:5px}
::-webkit-scrollbar-thumb:hover{background:#30404f}
@keyframes blink{0%,55%{opacity:1}56%,100%{opacity:.2}}
.mono{font-family:var(--mono)}
```

- [ ] **Step 3: Verify build**

Run (from `rust/`): `npm run build`
Expected: build succeeds (fonts resolve). Visual confirmed in Task 9.

- [ ] **Step 4: Commit**

```bash
git add rust/package.json rust/package-lock.json rust/src/ui/app/theme.css
git commit -m "feat(ui-shell): IBM Plex fonts + INU theme variables"
```

---

### Task 2: Clock formatting

**Files:**
- Create: `rust/src/ui/app/clock.ts`
- Test: `rust/src/ui/app/clock.test.ts`

**Interfaces:**
- Produces:
  - `formatClock(unixMs: number): { hms: string; ms: string }` — `hms` = `HH:MM:SS` (UTC), `ms` = 3-digit millis
  - `formatRideTag(tsMs: number): string` — ride time `MM:SS.mmm` from ms-since-start

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/app/clock.test.ts
import { describe, it, expect } from "vitest";
import { formatClock, formatRideTag } from "./clock";

describe("formatClock", () => {
  it("formats unix ms as HH:MM:SS + millis (UTC)", () => {
    // 1970-01-01 11:33:26.878 UTC = 41606878 ms
    const c = formatClock(41_606_878);
    expect(c.hms).toBe("11:33:26");
    expect(c.ms).toBe("878");
  });
});

describe("formatRideTag", () => {
  it("formats ms-since-start as MM:SS.mmm", () => {
    expect(formatRideTag(33 * 60_000 + 26_000 + 730)).toBe("33:26.730");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `rust/`): `npm test`
Expected: FAIL — cannot find module `./clock`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/ui/app/clock.ts
function p2(n: number): string { return n.toString().padStart(2, "0"); }
function p3(n: number): string { return n.toString().padStart(3, "0"); }

export function formatClock(unixMs: number): { hms: string; ms: string } {
  const d = new Date(unixMs);
  return {
    hms: `${p2(d.getUTCHours())}:${p2(d.getUTCMinutes())}:${p2(d.getUTCSeconds())}`,
    ms: p3(d.getUTCMilliseconds()),
  };
}

export function formatRideTag(tsMs: number): string {
  const totalSec = Math.floor(tsMs / 1000);
  const mm = Math.floor(totalSec / 60);
  const ss = totalSec % 60;
  const ms = tsMs % 1000;
  return `${p2(mm)}:${p2(ss)}.${p3(ms)}`;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/clock.ts rust/src/ui/app/clock.test.ts
git commit -m "feat(ui-shell): clock + ride-tag formatting"
```

---

### Task 3: Status derivation (alarms / cautions / link)

**Files:**
- Create: `rust/src/ui/app/status.ts`
- Test: `rust/src/ui/app/status.test.ts`

**Interfaces:**
- Consumes: `TelemetryStore`, `EnumValue` (via `store.enumIndex()`), `decodeEnum`
- Produces: `deriveStatus(store: TelemetryStore): { alarms: number; cautions: number; linkOk: boolean }`
  - `alarms` = count of channels whose current enum-decoded severity is `"critical"`
  - `cautions` = count of channels whose current enum-decoded severity is `"caution"`
  - `linkOk` = `store.channels().length > 0` (meta received)

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/app/status.test.ts
import { describe, it, expect } from "vitest";
import { deriveStatus } from "./status";
import { TelemetryStore } from "../../data/store";
import type { MetaMessage } from "../../types";

function meta(): MetaMessage {
  return {
    type: "meta", rate_hz: 10,
    enum_values: [
      { channel_id: 1, code: 0, label: "Normal", severity: "ok" },
      { channel_id: 1, code: 1, label: "Critical", severity: "critical" },
      { channel_id: 2, code: 0, label: "OK", severity: "ok" },
      { channel_id: 2, code: 1, label: "Warn", severity: "caution" },
    ],
    channels: [
      { id: 1, name: "M1", column_name: "m1", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 1, addr: "I_01" },
      { id: 2, name: "M2", column_name: "m2", unit: "-", type: "enum", min: 0, max: 1, widget: "table", display_order: 2, addr: "I_01" },
    ],
  };
}

describe("deriveStatus", () => {
  it("counts critical=alarm, caution, and link", () => {
    const s = new TelemetryStore();
    s.applyMeta(meta());
    s.applyFrame({ type: "frame", ts_ms: 0, emit_unix_ms: 1, values: [1, 1] }, 1);
    const st = deriveStatus(s);
    expect(st.alarms).toBe(1);   // m1 -> critical
    expect(st.cautions).toBe(1); // m2 -> caution
    expect(st.linkOk).toBe(true);
  });

  it("reports no link before meta", () => {
    const st = deriveStatus(new TelemetryStore());
    expect(st.linkOk).toBe(false);
    expect(st.alarms).toBe(0);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./status`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/ui/app/status.ts
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/status.ts rust/src/ui/app/status.test.ts
git commit -m "feat(ui-shell): derive alarm/caution/link status from store"
```

---

### Task 4: Tab model + transport labels

**Files:**
- Create: `rust/src/ui/app/tabs.ts`
- Test: `rust/src/ui/app/tabs.test.ts`

**Interfaces:**
- Produces:
  - `type Screen = "overview" | "track" | "events"`
  - `const SCREENS: { key: Screen; label: string }[]` — `[overview "OVERVIEW", track "FLIGHT TRACK", events "EVENTS"]`
  - `formatCount(n: number): string` — thousands-separated (e.g. `7451 -> "7,451"`)
  - `formatElapsed(ms: number): string` — `H:MM:SS` (e.g. `2h4m11s -> "2:04:11"`)

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/app/tabs.test.ts
import { describe, it, expect } from "vitest";
import { SCREENS, formatCount, formatElapsed } from "./tabs";

describe("tabs model", () => {
  it("lists the three screens in order", () => {
    expect(SCREENS.map((s) => s.key)).toEqual(["overview", "track", "events"]);
    expect(SCREENS[1].label).toBe("FLIGHT TRACK");
  });
});

describe("formatters", () => {
  it("formats counts with thousands separators", () => {
    expect(formatCount(7451)).toBe("7,451");
    expect(formatCount(0)).toBe("0");
  });
  it("formats elapsed as H:MM:SS", () => {
    expect(formatElapsed((2 * 3600 + 4 * 60 + 11) * 1000)).toBe("2:04:11");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./tabs`.

- [ ] **Step 3: Write minimal implementation**

```ts
// rust/src/ui/app/tabs.ts
export type Screen = "overview" | "track" | "events";

export const SCREENS: { key: Screen; label: string }[] = [
  { key: "overview", label: "OVERVIEW" },
  { key: "track", label: "FLIGHT TRACK" },
  { key: "events", label: "EVENTS" },
];

export function formatCount(n: number): string {
  return n.toLocaleString("en-US");
}

export function formatElapsed(ms: number): string {
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return `${h}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/tabs.ts rust/src/ui/app/tabs.test.ts
git commit -m "feat(ui-shell): screen model + count/elapsed formatters"
```

---

### Task 5: TopBar component

**Files:**
- Create: `rust/src/ui/app/TopBar.tsx`
- Test: `rust/src/ui/app/TopBar.test.tsx`

**Interfaces:**
- Consumes: `Screen`, `SCREENS`, `SystemStatus`
- Produces: `TopBar(props: { clock: { hms: string; ms: string }; status: SystemStatus; screen: Screen; onScreen: (s: Screen) => void; scalesOn: boolean; onToggleScales: () => void }): React.JSX.Element` — logo + AC/FLT id, the three nav tabs (active one highlighted, calls `onScreen`), ALARM/CAUTION pills (counts from `status`), SCALES toggle, LINK indicator, clock.

- [ ] **Step 1: Write the failing test**

```tsx
// rust/src/ui/app/TopBar.test.tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, cleanup, fireEvent } from "@testing-library/react";
import { TopBar } from "./TopBar";

afterEach(() => cleanup());

const base = {
  clock: { hms: "11:33:26", ms: "878" },
  status: { alarms: 2, cautions: 1, linkOk: true },
  screen: "overview" as const,
  onScreen: vi.fn(),
  scalesOn: true,
  onToggleScales: vi.fn(),
};

describe("TopBar", () => {
  it("renders brand, clock, and alarm/caution counts", () => {
    const { container } = render(<TopBar {...base} />);
    const t = container.textContent ?? "";
    expect(t).toContain("INU");
    expect(t).toContain("11:33:26");
    expect(t).toContain("2 ALARM");
    expect(t).toContain("1 CAUTION");
  });

  it("fires onScreen when a tab is clicked", () => {
    const onScreen = vi.fn();
    const { getByText } = render(<TopBar {...base} onScreen={onScreen} />);
    fireEvent.click(getByText("FLIGHT TRACK"));
    expect(onScreen).toHaveBeenCalledWith("track");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./TopBar`.

- [ ] **Step 3: Write minimal implementation**

```tsx
// rust/src/ui/app/TopBar.tsx
import type React from "react";
import { SCREENS, type Screen } from "./tabs";
import type { SystemStatus } from "./status";

export function TopBar({ clock, status, screen, onScreen, scalesOn, onToggleScales }: {
  clock: { hms: string; ms: string }; status: SystemStatus; screen: Screen;
  onScreen: (s: Screen) => void; scalesOn: boolean; onToggleScales: () => void;
}): React.JSX.Element {
  return (
    <div className="topbar">
      <div className="topbar-left">
        <div className="brand"><span className="brand-mark" /><div>
          <div className="brand-name">INU&middot;MONITOR</div>
          <div className="brand-sub mono">INERTIAL NAV TELEMETRY v4.0</div>
        </div></div>
        <div className="topbar-div" />
        <div className="mono ac-id">AC 4X-ELT <span className="dim">/</span> FLT 1182</div>
      </div>
      <div className="tabs">
        {SCREENS.map((s) => (
          <div key={s.key} className={`tab${screen === s.key ? " tab-on" : ""}`} onClick={() => onScreen(s.key)}>{s.label}</div>
        ))}
      </div>
      <div className="topbar-right">
        <div className="pill pill-alarm"><span className="dot dot-alarm" />{status.alarms} ALARM</div>
        <div className="pill pill-caution"><span className="dot dot-caution" />{status.cautions} CAUTION</div>
        <div className="scales" onClick={onToggleScales}>{scalesOn ? "SCALES ON" : "SCALES OFF"}</div>
        <div className="topbar-div" />
        <div className="link"><span className={`dot ${status.linkOk ? "dot-ok" : "dot-dim"}`} />LINK <span className="mono link-ok">{status.linkOk ? "1553B·OK" : "—"}</span></div>
        <div className="clock mono"><div className="clock-hms">{clock.hms}<span className="dim">.{clock.ms}</span></div></div>
      </div>
    </div>
  );
}
```
Add the matching styles to `theme.css` (or a `shell.css` imported by AppShell in Task 8) for `.topbar`, `.tab`/`.tab-on`, `.pill-alarm`/`.pill-caution`, `.dot-*`, `.scales`, `.link`, `.clock` using the spec palette. (Styling is visual; the smoke test only checks text + click.)

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/TopBar.tsx rust/src/ui/app/TopBar.test.tsx rust/src/ui/app/theme.css
git commit -m "feat(ui-shell): top bar (brand/tabs/alarms/link/clock)"
```

---

### Task 6: TransportBar component

**Files:**
- Create: `rust/src/ui/app/TransportBar.tsx`
- Test: `rust/src/ui/app/TransportBar.test.tsx`

**Interfaces:**
- Consumes: `formatCount`, `formatElapsed`
- Produces: `TransportBar(props: { clock: { hms: string; ms: string }; rideTag: string; rateHz: number; samples: number; elapsedMs: number; scrubberFrac: number }): React.JSX.Element` — transport buttons (visual), clock + ride tag + rate, a scrubber track with a position marker at `scrubberFrac` (0..1), and BUFFER/SAMPLES/DROPPED readouts.

- [ ] **Step 1: Write the failing test**

```tsx
// rust/src/ui/app/TransportBar.test.tsx
// @vitest-environment jsdom
import { describe, it, expect, afterEach } from "vitest";
import { render, cleanup } from "@testing-library/react";
import { TransportBar } from "./TransportBar";

afterEach(() => cleanup());

describe("TransportBar", () => {
  it("renders clock, samples count, and elapsed", () => {
    const { container } = render(
      <TransportBar clock={{ hms: "11:33:26", ms: "878" }} rideTag="33:26.730"
        rateHz={10} samples={7451} elapsedMs={(2 * 3600 + 4 * 60 + 11) * 1000} scrubberFrac={0.88} />,
    );
    const t = container.textContent ?? "";
    expect(t).toContain("11:33:26");
    expect(t).toContain("7,451");
    expect(t).toContain("2:04:11");
  });

  it("places the scrubber marker at the given fraction", () => {
    const { container } = render(
      <TransportBar clock={{ hms: "00:00:00", ms: "000" }} rideTag="0:00.000"
        rateHz={10} samples={0} elapsedMs={0} scrubberFrac={0.5} />,
    );
    const marker = container.querySelector('[data-testid="scrub-marker"]') as HTMLElement;
    expect(marker.style.left).toBe("50%");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./TransportBar`.

- [ ] **Step 3: Write minimal implementation**

```tsx
// rust/src/ui/app/TransportBar.tsx
import type React from "react";
import { formatCount, formatElapsed } from "./tabs";

export function TransportBar({ clock, rideTag, rateHz, samples, elapsedMs, scrubberFrac }: {
  clock: { hms: string; ms: string }; rideTag: string; rateHz: number;
  samples: number; elapsedMs: number; scrubberFrac: number;
}): React.JSX.Element {
  const pct = `${Math.min(100, Math.max(0, scrubberFrac * 100))}%`;
  return (
    <div className="transport">
      <div className="transport-ctrl">
        <div className="tbtns"><span className="tbtn">⏮</span><span className="tbtn tbtn-on">⏸</span><span className="tbtn">⏭</span></div>
        <div className="mono"><div className="t-clock">{clock.hms}<span className="dim">.{clock.ms}</span></div>
          <div className="t-sub">T+{rideTag} · {rateHz.toFixed(1)} s/s</div></div>
      </div>
      <div className="transport-scrub">
        <div className="scrub-track">
          <div className="scrub-fill" style={{ width: pct }} />
          <div className="scrub-marker" data-testid="scrub-marker" style={{ left: pct }} />
        </div>
      </div>
      <div className="transport-stats mono">
        <div><div className="t-lbl">BUFFER</div><div className="t-val">{formatElapsed(elapsedMs)}</div></div>
        <div><div className="t-lbl">SAMPLES</div><div className="t-val">{formatCount(samples)}</div></div>
        <div><div className="t-lbl">DROPPED</div><div className="t-val t-ok">0</div></div>
      </div>
    </div>
  );
}
```
Add matching `.transport*` styles to `theme.css`/`shell.css`.

- [ ] **Step 4: Run test to verify it passes**

Run: `npm test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/TransportBar.tsx rust/src/ui/app/TransportBar.test.tsx rust/src/ui/app/theme.css
git commit -m "feat(ui-shell): bottom transport timeline bar"
```

---

### Task 7: Tab stub views

**Files:**
- Create: `rust/src/ui/app/views/OverviewView.tsx`, `TrackView.tsx`, `EventsView.tsx`

**Interfaces:**
- Produces: three stub components, each `({ store }: { store: TelemetryStore }) => React.JSX.Element`, rendering a placeholder panel with the screen name and a `data-testid` of `view-overview` / `view-track` / `view-events`. These are replaced by real content in Phases 2-6.

- [ ] **Step 1: Write the stubs**

```tsx
// rust/src/ui/app/views/OverviewView.tsx
import type React from "react";
import type { TelemetryStore } from "../../../data/store";
export function OverviewView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return <div data-testid="view-overview" className="view-stub">OVERVIEW — {store.channels().length} channels</div>;
}
```
```tsx
// rust/src/ui/app/views/TrackView.tsx
import type React from "react";
import type { TelemetryStore } from "../../../data/store";
export function TrackView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return <div data-testid="view-track" className="view-stub">FLIGHT TRACK — {store.gpsTrack().lat.length} pts</div>;
}
```
```tsx
// rust/src/ui/app/views/EventsView.tsx
import type React from "react";
import type { TelemetryStore } from "../../../data/store";
export function EventsView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return <div data-testid="view-events" className="view-stub">EVENTS — {store.metrics() ? "metrics" : "no metrics"}</div>;
}
```

- [ ] **Step 2: Verify build**

Run (from `rust/`): `npx tsc --noEmit && npm run build`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add rust/src/ui/app/views
git commit -m "feat(ui-shell): tab stub views (overview/track/events)"
```

---

### Task 8: AppShell + wire App.tsx

**Files:**
- Create: `rust/src/ui/app/AppShell.tsx`
- Modify: `rust/src/App.tsx`
- Modify: `rust/src/ui/app/theme.css` (shell layout)

**Interfaces:**
- Consumes: `useTelemetry`, `TopBar`, `TransportBar`, the stub views, `formatClock`/`formatRideTag`, `deriveStatus`, `FpsMeter` (already in the hook)
- Produces: `AppShell(): React.JSX.Element` — full-height column: `TopBar` / active tab view / `TransportBar`. Holds `screen` + `scalesOn` state and `useTelemetry(WS_URL)`. Derives clock from `Date.now()` (re-read each render via the hook's version tick), status from the store, ride tag + sample estimate from `store.lastEmitUnixMs` / a frame counter.

- [ ] **Step 1: Write the failing test**

```tsx
// rust/src/ui/app/AppShell.test.tsx
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, cleanup, fireEvent } from "@testing-library/react";
import { AppShell } from "./AppShell";

beforeEach(() => {
  let id = 0;
  vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => { id++; setTimeout(() => cb(id), 0); return id; });
  vi.stubGlobal("cancelAnimationFrame", () => {});
});
afterEach(() => cleanup());

describe("AppShell", () => {
  it("shows the overview view by default and switches tabs", () => {
    const { container, getByText } = render(<AppShell />);
    expect(container.querySelector('[data-testid="view-overview"]')).not.toBeNull();
    fireEvent.click(getByText("EVENTS"));
    expect(container.querySelector('[data-testid="view-events"]')).not.toBeNull();
    expect(container.querySelector('[data-testid="view-overview"]')).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm test`
Expected: FAIL — cannot find module `./AppShell`.

- [ ] **Step 3: Write minimal implementation**

```tsx
// rust/src/ui/app/AppShell.tsx
import { useState } from "react";
import type React from "react";
import "./theme.css";
import { useTelemetry } from "../useTelemetry";
import { TopBar } from "./TopBar";
import { TransportBar } from "./TransportBar";
import { OverviewView } from "./views/OverviewView";
import { TrackView } from "./views/TrackView";
import { EventsView } from "./views/EventsView";
import { formatClock, formatRideTag } from "./clock";
import { deriveStatus } from "./status";
import type { Screen } from "./tabs";

const WS_URL = (import.meta.env.VITE_WS_URL as string | undefined) ?? "ws://127.0.0.1:9001";

export function AppShell(): React.JSX.Element {
  const { store } = useTelemetry(WS_URL);
  const [screen, setScreen] = useState<Screen>("overview");
  const [scalesOn, setScalesOn] = useState(true);

  const now = Date.now();
  const clock = formatClock(now);
  const status = deriveStatus(store);
  const rideMs = store.lastEmitUnixMs() > 0 ? store.lastEmitUnixMs() : 0;
  const rideTag = formatRideTag(0); // ride ts wiring refined in a later phase
  const channels = store.channels();
  const rateHz = 10;
  const samples = channels.length > 0 ? store.series(channels[0].id)?.len() ?? 0 : 0;

  return (
    <div className="app-shell">
      <TopBar clock={clock} status={status} screen={screen} onScreen={setScreen}
        scalesOn={scalesOn} onToggleScales={() => setScalesOn((v) => !v)} />
      <div className="app-body">
        {screen === "overview" && <OverviewView store={store} />}
        {screen === "track" && <TrackView store={store} />}
        {screen === "events" && <EventsView store={store} />}
      </div>
      <TransportBar clock={clock} rideTag={rideTag} rateHz={rateHz}
        samples={samples} elapsedMs={now - (now - rideMs)} scrubberFrac={0.88} />
    </div>
  );
}
```

```tsx
// rust/src/App.tsx
import { AppShell } from "./ui/app/AppShell";
export default function App() { return <AppShell />; }
```

Add shell layout CSS to `theme.css`:
```css
.app-shell{height:100vh;display:flex;flex-direction:column;overflow:hidden}
.app-body{flex:1;min-height:0;position:relative;overflow:hidden}
.view-stub{padding:16px;color:var(--title);font-family:var(--mono)}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `rust/`): `npm test`
Expected: PASS (all suites — the new shell tests + the existing data-layer + old-component tests).

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/AppShell.tsx rust/src/ui/app/AppShell.test.tsx rust/src/App.tsx rust/src/ui/app/theme.css
git commit -m "feat(ui-shell): AppShell composing top bar, tab views, transport"
```

---

### Task 9: Build + live verify

This task is manual. No new unit tests.

- [ ] **Step 1: Full check** — from `rust/`: `npx tsc --noEmit && npm test && npm run build`. All green.

- [ ] **Step 2: Launch** — generate a DB if needed (`python data/simulate.py --out data/ride_small.db --duration 60`), then `RIDE_DB=../../data/ride_small.db RIDE_SPEED=5 npm run tauri dev`. Confirm: dark INU theme + IBM Plex fonts; top bar with brand, three tabs that switch the body stub, ALARM/CAUTION pills, LINK, live clock; bottom transport bar with samples/elapsed counting. Compare against `docs/sample ui/screenshots/overview-full.png` (top bar + transport regions).

- [ ] **Step 3: Commit any visual tweaks**

```bash
git add rust/
git commit -m "feat(ui-shell): align shell visuals with the reference"
```

---

## Self-Review

**Spec coverage (Phase 1 of the design spec):** theme + fonts (Task 1); top bar brand/tabs/alarm·caution/link/clock/scales (Tasks 3,5); tab routing (Tasks 4,8); transport timeline (Tasks 4,6); live wiring via `useTelemetry`/store (Tasks 3,8). Tab content is intentionally stubbed (Task 7) — filled in Phases 2-6. ✓

**Placeholder scan:** No TBD/TODO. `rideTag`/`elapsedMs` use a simplified ride-time source in Task 8 with an explicit note that exact ride-time wiring is refined in a later phase — that is a scoped simplification, not a missing requirement (the transport math is unit-tested in Task 4). ✓

**Type consistency:** `Screen`/`SCREENS` (Task 4) used by `TopBar` (5) + `AppShell` (8). `SystemStatus`/`deriveStatus` (3) used by `TopBar` + `AppShell`. `formatClock`/`formatRideTag` (2) and `formatCount`/`formatElapsed` (4) used by the components. All read the existing `TelemetryStore` selectors. ✓

> **Note for Phase 2:** the `OverviewView` stub is the integration point — Phase 2 replaces it with the grouped param panel (+ the widget grid in Phase 3). Group channels by a static table keyed on `column_name` per the design spec.
