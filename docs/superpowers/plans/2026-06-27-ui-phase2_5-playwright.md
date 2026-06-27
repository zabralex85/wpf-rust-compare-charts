# INU-MONITOR Rewrite — Phase 2.5: Playwright E2E + mock-WS

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real-browser E2E + visual-regression testing to the Rust web frontend with **Playwright**, made deterministic by a **mock-WS mode** (`?mock=1`) that feeds the store a fixed canned snapshot instead of the live WebSocket. This gives Phases 3-7 a way to test layout/canvas/interactions (impossible in jsdom) and to lock the look via screenshot baselines.

**Architecture:** A mock fixture (`src/ui/mock/`) holds a deterministic meta + frames; `applyMockSnapshot(store)` applies them; `useTelemetry` enters mock mode when the URL has `?mock=1` (no WS, store fed once, rAF tick still runs so the app renders normally — but with fixed data the whole UI is static and screenshot-stable). Playwright boots the Vite dev server and drives Chromium. vitest stays the per-task unit gate; Playwright is a separate pre-merge suite.

**Tech Stack:** `@playwright/test`, Vite dev server, the existing React app + data layer (unchanged behavior; mock mode is an additive branch in `useTelemetry`).

## Global Constraints

- Mock mode must be **opt-in** (`?mock=1` query param) — default behavior (live WS) unchanged. With mock, `useTelemetry` must NOT open a WebSocket.
- The fixture is deterministic: fixed channels (the real 30), fixed enum (`inu_mode2` critical), and frames with a fixed latest `ts_ms` so clock/`T+`/scrubber/values are all static.
- Playwright runs **headless Chromium**; the config starts `npm run dev` (Vite on `:1420`) as its `webServer`. Screenshot baselines are generated on first run and committed.
- Keep the vitest node/jsdom split intact; Playwright specs live under `rust/e2e/` and are NOT picked up by vitest (`include` is `src/**`).
- TS strict; no `any` in exported signatures.

---

### Task 1: Mock fixture + snapshot applier

**Files:**
- Create: `rust/src/ui/mock/fixture.ts`
- Create: `rust/src/ui/mock/fixture.test.ts`

**Interfaces:**
- Produces:
  - `MOCK_META: MetaMessage` — 30 realistic channels (mirror the simulator's set: roll/pitch/heading_t/heading_m/acc_x.. etc with sane min/max/units/widgets/display_order/addr), `enum_values` incl. `inu_mode2` Normal/Critical, `rate_hz: 10`, `duration_s: 600`.
  - `MOCK_FRAMES: FrameMessage[]` — a few frames; the last has `ts_ms` = e.g. `41_606_878` (so the mission clock shows a recognizable time) and `values` including a Critical `inu_mode2`.
  - `applyMockSnapshot(store: TelemetryStore): void` — applies `MOCK_META` then each of `MOCK_FRAMES` in order.

- [ ] **Step 1: Write the failing test**

```ts
// rust/src/ui/mock/fixture.test.ts
import { describe, it, expect } from "vitest";
import { MOCK_META, MOCK_FRAMES, applyMockSnapshot } from "./fixture";
import { TelemetryStore } from "../../data/store";

describe("mock fixture", () => {
  it("has 30 channels, rate, duration, and an inu_mode2 enum", () => {
    expect(MOCK_META.channels.length).toBe(30);
    expect(MOCK_META.rate_hz).toBe(10);
    expect(MOCK_META.duration_s).toBeGreaterThan(0);
    expect(MOCK_META.enum_values.some((e) => e.severity === "critical")).toBe(true);
    expect(MOCK_FRAMES.length).toBeGreaterThan(0);
  });

  it("applies to a store deterministically", () => {
    const s = new TelemetryStore();
    applyMockSnapshot(s);
    expect(s.channels().length).toBe(30);
    expect(s.lastTsMs()).toBe(MOCK_FRAMES[MOCK_FRAMES.length - 1].ts_ms);
    const roll = s.channels().find((c) => c.column_name === "roll")!;
    expect(s.latest(roll.id)).toBeDefined();
  });
});
```

Run (from `rust/`): `npm test` → FAIL.

- [ ] **Step 2: Implement** — write `fixture.ts` with `MOCK_META` (30 channels matching the simulator schema; values realistic), `MOCK_FRAMES` (≥3 frames, last `ts_ms = 41_606_878`, one channel set so `inu_mode2` decodes Critical), and:
```ts
export function applyMockSnapshot(store: TelemetryStore): void {
  store.applyMeta(MOCK_META);
  for (const f of MOCK_FRAMES) store.applyFrame(f);
}
```
(Use the real channel set + `display_order` so `groupChannels` buckets them like production.)

- [ ] **Step 3: Verify** — `npm test` + `npx tsc --noEmit`.
- [ ] **Step 4: Commit** — `git add rust/src/ui/mock && git commit -m "feat(rust-ui): deterministic mock telemetry fixture"`

---

### Task 2: useTelemetry mock mode

**Files:**
- Modify: `rust/src/ui/useTelemetry.ts`
- Test: `rust/src/ui/useTelemetry.test.tsx` (extend)

**Interfaces:**
- Produces: `useTelemetry(url)` — if the page URL has `?mock=1` (or `import.meta.env.VITE_MOCK === "1"`), it calls `applyMockSnapshot(store)` once and sets status `"open"` **without** opening a WebSocket; otherwise unchanged. The rAF tick still runs.

- [ ] **Step 1: Write the failing test** (extend the hook test)

```tsx
  it("uses the mock snapshot when ?mock=1 (no socket factory needed)", () => {
    // jsdom default location has no query; set it
    window.history.pushState({}, "", "/?mock=1");
    const { result } = renderHook(() => useTelemetry("ws://127.0.0.1:9999"));
    expect(result.current.store.channels().length).toBe(30);
    window.history.pushState({}, "", "/");
  });
```
(Keep the existing rAF stub from `beforeEach`.)

Run: `npm test` → FAIL (store empty without a real socket).

- [ ] **Step 2: Implement** — in `useTelemetry`'s effect, compute `const mock = typeof window !== "undefined" && new URLSearchParams(window.location.search).has("mock");` (or the env). If `mock`: `applyMockSnapshot(storeRef.current!); setStatus("open");` and skip `createWsClient`; the cleanup just `cancelAnimationFrame(raf)`. Else the existing path. Import `applyMockSnapshot` from `./mock/fixture`.

- [ ] **Step 3: Verify** — `npm test` (all green) + `npx tsc --noEmit` + `npm run build`.
- [ ] **Step 4: Commit** — `git add rust/src/ui/useTelemetry.ts rust/src/ui/useTelemetry.test.tsx && git commit -m "feat(rust-ui): useTelemetry mock mode via ?mock=1"`

---

### Task 3: Playwright install + config

**Files:**
- Modify: `rust/package.json` (devDep + scripts)
- Create: `rust/playwright.config.ts`
- Modify: `rust/.gitignore` (test-results, playwright-report)

**Interfaces:**
- Produces: `@playwright/test` installed with Chromium; `playwright.config.ts` with `testDir: "e2e"`, `webServer` running `npm run dev` on `http://localhost:1420`, `use.baseURL`, a `chromium` project, and `expect.toHaveScreenshot` defaults (a small `maxDiffPixelRatio`).

- [ ] **Step 1: Install**

Run (from `rust/`):
```bash
npm install -D @playwright/test
npx playwright install chromium
```

- [ ] **Step 2: Config**

```ts
// rust/playwright.config.ts
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  retries: 0,
  use: { baseURL: "http://localhost:1420", trace: "on-first-retry" },
  expect: { toHaveScreenshot: { maxDiffPixelRatio: 0.02 } },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"], viewport: { width: 1600, height: 900 } } }],
  webServer: { command: "npm run dev", url: "http://localhost:1420", reuseExistingServer: !process.env.CI, timeout: 60_000 },
});
```

- [ ] **Step 3: Scripts + gitignore** — add to `package.json` scripts: `"e2e": "playwright test"`, `"e2e:update": "playwright test --update-snapshots"`. Add to `rust/.gitignore`: `test-results/`, `playwright-report/`, `playwright/.cache/`.

- [ ] **Step 4: Verify it loads** — `npx playwright test --list` (no specs yet → "no tests found" is acceptable; it must not error on config). Confirm Chromium installed.

- [ ] **Step 5: Commit** — `git add rust/package.json rust/package-lock.json rust/playwright.config.ts rust/.gitignore && git commit -m "test(rust-ui): add Playwright + config"`

---

### Task 4: First E2E spec + screenshot baseline

**Files:**
- Create: `rust/e2e/shell.spec.ts`

**Interfaces:**
- Produces: a Playwright spec that loads `/?mock=1` and verifies the shell + param panel render the canned data, tabs switch, and a screenshot matches the committed baseline.

- [ ] **Step 1: Write the spec**

```ts
// rust/e2e/shell.spec.ts
import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => { await page.goto("/?mock=1"); });

test("top bar + tabs + param panel render the mock data", async ({ page }) => {
  await expect(page.getByText("INU·MONITOR")).toBeVisible();
  // param panel groups + a channel
  await expect(page.getByText("PARAMETERS")).toBeVisible();
  await expect(page.getByText("Attitude")).toBeVisible();
  await expect(page.getByText("Roll", { exact: false })).toBeVisible();
  // tab switching
  await page.getByText("EVENTS", { exact: true }).click();
  await expect(page.getByTestId("view-events")).toBeVisible();
  await page.getByText("OVERVIEW", { exact: true }).click();
  await expect(page.getByTestId("view-overview")).toBeVisible();
});

test("overview matches the visual baseline", async ({ page }) => {
  // let fonts settle
  await page.waitForTimeout(400);
  await expect(page).toHaveScreenshot("overview.png", { fullPage: false });
});
```

- [ ] **Step 2: Generate the baseline + run**

Run (from `rust/`):
```bash
npm run e2e:update   # creates e2e/shell.spec.ts-snapshots/overview-*.png
npm run e2e          # both tests pass against the baseline
```
Expected: the functional test passes; the screenshot test passes against the just-created baseline. (If `e2e` flakes on first run because the server is still warming, re-run — `reuseExistingServer` keeps it up.)

- [ ] **Step 3: Sanity-check the baseline** — open the generated `overview-*.png` and confirm it shows the themed shell + param panel (not a blank/error page). It's the visual oracle going forward; eyeball it against `docs/sample ui/screenshots/overview-full.png` for the top-bar/param regions.

- [ ] **Step 4: Commit** (include the snapshot PNGs) — `git add rust/e2e && git commit -m "test(rust-ui): first Playwright E2E + overview screenshot baseline"`

---

### Task 5: Docs + final verify

**Files:**
- Modify: `rust/README.md`

- [ ] **Step 1: Document** — add a "Tests" subsection to `rust/README.md`: `npm test` (vitest units), `npm run e2e` (Playwright E2E/visual, uses `?mock=1` deterministic data), `npm run e2e:update` (refresh baselines after intentional UI changes). Note baselines are Chromium/OS-specific.
- [ ] **Step 2: Full check** — from `rust/`: `npx tsc --noEmit && npm test && npm run build && npm run e2e`. All green. From `rust/src-tauri`: `cargo test`.
- [ ] **Step 3: Commit** — `git add rust/README.md && git commit -m "docs(rust-ui): document vitest + Playwright test suites"`

---

## Self-Review

**Spec coverage:** deterministic mock-WS mode (Tasks 1-2); Playwright install/config (Task 3); first real-browser E2E + visual baseline (Task 4); docs + green suite (Task 5). ✓
**Placeholder scan:** No TBD. The screenshot baseline is generated in Task 4 (not a placeholder — it's the artifact the visual test enforces). ✓
**Type consistency:** `MOCK_META`/`MOCK_FRAMES`/`applyMockSnapshot` (Task 1) consumed by `useTelemetry` mock mode (Task 2). Playwright config (Task 3) drives the specs (Task 4). The mock branch in `useTelemetry` is additive — live WS path unchanged. ✓

> **Note for Phase 3+:** add an E2E + screenshot per new region (gauge/line/map widgets, interactions) under `rust/e2e/`, always loading `?mock=1`. For interaction tests (drag/resize/hover), Playwright's mouse API drives the real DOM. Refresh baselines with `npm run e2e:update` only for intentional visual changes.
