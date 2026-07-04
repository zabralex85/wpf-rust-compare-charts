# Rust Dioxus-native variant (Blitz/Vello) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a third dashboard implementation — a Rust UI rendered natively (no WebView) via `dioxus-native` (Blitz + Vello) — to beat the WebView2 RAM floor and add an honest third row to the README footprint table.

**Architecture:** New standalone `rust-native/` binary crate. Reuses the existing Rust backend logic (`app_lib`: `db`, `replay`, `metrics`) via a path dependency — zero changes to `rust/src-tauri`. Replay runs in-process (no WebSocket) feeding a windowed store; a Dioxus RSX tree renders a parameter table + perf HUD in HTML/CSS (Blitz) and strip charts via a custom canvas element painted with Vello `BezPath`.

**Tech Stack:** Rust, `dioxus` + `dioxus-native` (Blitz/Vello/WGPU), `app_lib` (path dep), `rusqlite` (transitively via app_lib), `sysinfo` (via app_lib metrics).

## Global Constraints

- **Zero changes to `rust/src-tauri`** (the existing Tauri app). This variant only adds the new `rust-native/` crate + README edits.
- Reuse backend logic via a **path dependency on `app_lib`** (`path = "../rust/src-tauri"`, `package = "app_lib"`). Do not copy or reimplement `db`/`replay`/`metrics`.
- **Native renderer only** — `dioxus-native` (Blitz/Vello). Never add `dioxus-desktop`/`wry` (that would reintroduce the WebView2 floor).
- **Pin exact versions** of `dioxus`, `dioxus-native`, and any `vello`/`blitz-*` crates in `rust-native/Cargo.toml`; commit `Cargo.lock`. Experimental crates — a breaking upgrade is a separate chore, not part of this plan.
- **Fairness (CLAUDE.md):** strip-chart window = **60_000 ms**; metrics from `app_lib::metrics::MetricsSampler` (sysinfo, own PID); replay paced by `app_lib::replay::Pacer` honoring `RIDE_SPEED`. Match the other stacks' eviction boundary and metrics cadence.
- **v1 scope:** parameter table + strip charts + perf HUD only. No map, gauges, interactive grid, or transport controls.
- **Testing convention (CLAUDE.md):** pure logic (`data.rs`, `feed.rs`, `chart.rs` geometry) is unit-tested (TDD); Vello paint + RSX + window are **build-verified + launch-confirmed**, not unit-tested.
- `RIDE_DB` resolves like the Tauri app (env `RIDE_DB`, else `../data/ride.db` / `../data/ride_small.db`). Tests use the committed `data/ride_small.db` fixture (30 channels, 100 samples, 10 s).

**Reused `app_lib` API (verbatim signatures — do not redefine):**
- `app_lib::db::ChannelMeta { id: i64, name: String, column_name: String, unit: String, type_: String, min: f64, max: f64, widget: String, display_order: i64, addr: String }`
- `app_lib::db::Sample { ts_ms: i64, values: Vec<f64> }`
- `app_lib::db::load_channels(&rusqlite::Connection) -> rusqlite::Result<Vec<ChannelMeta>>`
- `app_lib::db::load_samples(&rusqlite::Connection, &[ChannelMeta]) -> rusqlite::Result<Vec<Sample>>`
- `app_lib::replay::Pacer::new(speed: f64)`; `Pacer::due_offset_ms(&self, sample_ts_ms: i64) -> i64`
- `app_lib::metrics::MetricsSampler::new()`; `MetricsSampler::sample(&mut self) -> app_lib::metrics::Metrics { cpu_pct: f32, ram_mb: f64 }`

---

### Task 1: Scaffold `rust-native` crate + native hello-window

**Files:**
- Create: `rust-native/Cargo.toml`
- Create: `rust-native/src/main.rs`
- Create: `rust-native/.gitignore` (`/target`)

**Interfaces:**
- Produces: a runnable binary `rust-native` that opens a native (non-WebView) window rendering a text banner.

- [ ] **Step 1: Create the crate manifest**

`rust-native/Cargo.toml`:

```toml
[package]
name = "rust-native"
version = "0.1.0"
edition = "2021"

[dependencies]
app_lib = { path = "../rust/src-tauri", package = "app_lib" }
# Pinned in Step 2 via `cargo add` — do not hand-guess versions.

[[bin]]
name = "rust-native"
path = "src/main.rs"
```

- [ ] **Step 2: Add the native renderer deps (pinned)**

Run:
```bash
cd rust-native
cargo add dioxus
cargo add dioxus-native
```
Then open `Cargo.toml` and replace the `^`/caret ranges `cargo add` wrote with the **exact** resolved versions (read them from `Cargo.lock`), e.g. `dioxus = "=0.7.x"`, `dioxus-native = "=0.7.x"`. Record the versions in a comment. If `dioxus-native` fails to resolve on crates.io, pin a git rev of `DioxusLabs/dioxus` instead and note the rev.

- [ ] **Step 3: Minimal native window**

`rust-native/src/main.rs`:

```rust
use dioxus::prelude::*;

fn main() {
    // Launch via the NATIVE renderer (Blitz/Vello) — NOT dioxus-desktop.
    dioxus_native::launch(app);
}

fn app() -> Element {
    rsx! {
        div {
            style: "background:#0a0e14;color:#38c5e0;font-family:sans-serif;padding:24px;height:100vh;",
            h1 { "INU-NATIVE" }
            p { "Blitz + Vello — no WebView" }
        }
    }
}
```
> If the current `dioxus-native` launch entrypoint differs (experimental API), follow the crate's docs.rs example for `launch`; keep `app()` returning the same `rsx!`.

- [ ] **Step 4: Build + launch verify**

Run:
```bash
cd rust-native && cargo run
```
Expected: a native window opens showing "INU-NATIVE" on a dark background. Confirm via Task Manager there is **no `msedgewebview2.exe`** child — only the `rust-native` process. Close the window.

- [ ] **Step 5: Commit**

```bash
git add rust-native/Cargo.toml rust-native/Cargo.lock rust-native/src/main.rs rust-native/.gitignore
git commit -m "feat(rust-native): scaffold dioxus-native crate + hello window"
```

---

### Task 2: Windowed strip buffer (`data.rs`, pure, TDD)

**Files:**
- Create: `rust-native/src/data.rs`
- Modify: `rust-native/src/main.rs` (add `mod data;`)

**Interfaces:**
- Produces:
  - `data::WindowBuffer::new(window_ms: i64) -> WindowBuffer`
  - `WindowBuffer::push(&mut self, ts_ms: i64, value: f64)` — appends, then evicts points with `ts < ts_ms - window_ms`.
  - `WindowBuffer::points(&self) -> &[(i64, f64)]`
  - `WindowBuffer::len(&self) -> usize`

- [ ] **Step 1: Write failing tests**

`rust-native/src/data.rs`:

```rust
//! Windowed strip-series buffer — Rust port of the Tauri frontend ringBuffer.ts.
//! Keeps only points within `window_ms` of the newest timestamp.

pub struct WindowBuffer {
    window_ms: i64,
    points: Vec<(i64, f64)>,
}

impl WindowBuffer {
    pub fn new(window_ms: i64) -> Self {
        Self { window_ms, points: Vec::new() }
    }

    pub fn push(&mut self, ts_ms: i64, value: f64) {
        self.points.push((ts_ms, value));
        let cutoff = ts_ms - self.window_ms;
        // evict from the front while older than the window's left edge
        let drop = self.points.iter().take_while(|(t, _)| *t < cutoff).count();
        if drop > 0 {
            self.points.drain(0..drop);
        }
    }

    pub fn points(&self) -> &[(i64, f64)] {
        &self.points
    }

    pub fn len(&self) -> usize {
        self.points.len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accumulates_within_window() {
        let mut b = WindowBuffer::new(1000);
        b.push(0, 1.0);
        b.push(500, 2.0);
        b.push(1000, 3.0);
        assert_eq!(b.len(), 3);
        assert_eq!(b.points()[0], (0, 1.0));
    }

    #[test]
    fn evicts_points_older_than_window() {
        let mut b = WindowBuffer::new(1000);
        b.push(0, 1.0);
        b.push(1001, 2.0); // cutoff = 1, so ts=0 is evicted
        assert_eq!(b.len(), 1);
        assert_eq!(b.points()[0], (1001, 2.0));
    }

    #[test]
    fn keeps_point_exactly_on_the_window_edge() {
        let mut b = WindowBuffer::new(1000);
        b.push(0, 1.0);
        b.push(1000, 2.0); // cutoff = 0, ts=0 is NOT < 0 -> kept
        assert_eq!(b.len(), 2);
    }
}
```

- [ ] **Step 2: Wire the module**

Add to the top of `rust-native/src/main.rs`: `mod data;`

- [ ] **Step 3: Run tests to verify they pass**

Run: `cd rust-native && cargo test data::`
Expected: 3 tests pass.

- [ ] **Step 4: Commit**

```bash
git add rust-native/src/data.rs rust-native/src/main.rs
git commit -m "feat(rust-native): windowed strip buffer (60s port of ringBuffer)"
```

---

### Task 3: In-process replay feed (`feed.rs`, integration, TDD with fixture)

**Files:**
- Create: `rust-native/src/feed.rs`
- Modify: `rust-native/src/main.rs` (add `mod feed;`)

**Interfaces:**
- Consumes: `app_lib::db::{load_channels, load_samples, ChannelMeta, Sample}`, `app_lib::replay::Pacer`.
- Produces:
  - `feed::Feed::open(db_path: &str, speed: f64) -> rusqlite::Result<Feed>` — loads channels + samples, builds a `Pacer`.
  - `Feed::channels(&self) -> &[ChannelMeta]`
  - `Feed::strip_indices(&self) -> &[usize]` — column indices where `widget == "strip"`.
  - `Feed::due_upto(&mut self, elapsed_ms: i64) -> &[Sample]` — returns the slice of not-yet-consumed samples whose `due_offset_ms(ts) <= elapsed_ms`, advancing an internal cursor. (Deterministic: no wall clock inside.)

- [ ] **Step 1: Write failing tests**

`rust-native/src/feed.rs`:

```rust
//! In-process replay feed: loads the ride once, hands out samples as they
//! become "due" for a given elapsed wall time. No socket (mirrors the .NET
//! app's in-process idiom). Deterministic — the caller supplies elapsed_ms.

use app_lib::db::{load_channels, load_samples, ChannelMeta, Sample};
use app_lib::replay::Pacer;
use rusqlite::Connection;

pub struct Feed {
    channels: Vec<ChannelMeta>,
    samples: Vec<Sample>,
    strip_idx: Vec<usize>,
    pacer: Pacer,
    cursor: usize,
}

impl Feed {
    pub fn open(db_path: &str, speed: f64) -> rusqlite::Result<Self> {
        let conn = Connection::open(db_path)?;
        let channels = load_channels(&conn)?;
        let samples = load_samples(&conn, &channels)?;
        let strip_idx = channels
            .iter()
            .enumerate()
            .filter(|(_, c)| c.widget == "strip")
            .map(|(i, _)| i)
            .collect();
        Ok(Self { channels, samples, strip_idx, pacer: Pacer::new(speed), cursor: 0 })
    }

    pub fn channels(&self) -> &[ChannelMeta] {
        &self.channels
    }

    pub fn strip_indices(&self) -> &[usize] {
        &self.strip_idx
    }

    pub fn due_upto(&mut self, elapsed_ms: i64) -> &[Sample] {
        let start = self.cursor;
        while self.cursor < self.samples.len()
            && self.pacer.due_offset_ms(self.samples[self.cursor].ts_ms) <= elapsed_ms
        {
            self.cursor += 1;
        }
        &self.samples[start..self.cursor]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const FIXTURE: &str = "../data/ride_small.db";

    #[test]
    fn loads_thirty_channels_with_five_strips() {
        let f = Feed::open(FIXTURE, 1.0).unwrap();
        assert_eq!(f.channels().len(), 30);
        assert_eq!(f.strip_indices().len(), 5); // roll,pitch,acc_x,acc_y,acc_z
    }

    #[test]
    fn due_upto_yields_samples_up_to_elapsed_at_speed_1() {
        let mut f = Feed::open(FIXTURE, 1.0).unwrap();
        // 10 Hz fixture: ts = 0,100,200,... at speed 1 due_offset == ts.
        let batch = f.due_upto(250).len(); // ts 0,100,200 due
        assert_eq!(batch, 3);
        let more = f.due_upto(500).len(); // ts 300,400,500
        assert_eq!(more, 3);
    }

    #[test]
    fn cursor_does_not_replay_consumed_samples() {
        let mut f = Feed::open(FIXTURE, 1.0).unwrap();
        let _ = f.due_upto(1_000_000); // drain all
        assert_eq!(f.due_upto(1_000_000).len(), 0);
    }
}
```

- [ ] **Step 2: Wire the module**

Add to `rust-native/src/main.rs`: `mod feed;`

- [ ] **Step 3: Run tests**

Run: `cd rust-native && cargo test feed::`
Expected: 3 tests pass (needs `data/ride_small.db` present; it is committed).

- [ ] **Step 4: Commit**

```bash
git add rust-native/src/feed.rs rust-native/src/main.rs
git commit -m "feat(rust-native): in-process replay feed reusing app_lib db+pacer"
```

---

### Task 4: Chart geometry (`chart.rs`, pure, TDD)

**Files:**
- Create: `rust-native/src/chart.rs`
- Modify: `rust-native/src/main.rs` (add `mod chart;`)

**Interfaces:**
- Consumes: `data::WindowBuffer`.
- Produces:
  - `chart::to_screen(points: &[(i64, f64)], window_ms: i64, w: f32, h: f32, min: f64, max: f64) -> Vec<(f32, f32)>` — maps each `(ts, value)` to canvas pixels. x: newest point at the right edge (`x = w - (newest_ts - ts)/window_ms * w`); y: value inverted into `[0, h]` clamped to `[min, max]`.

- [ ] **Step 1: Write failing tests**

`rust-native/src/chart.rs`:

```rust
//! Pure strip-chart geometry: (ts, value) samples -> canvas pixel points.
//! The newest sample sits at the right edge; the window's left edge is x=0.

pub fn to_screen(
    points: &[(i64, f64)],
    window_ms: i64,
    w: f32,
    h: f32,
    min: f64,
    max: f64,
) -> Vec<(f32, f32)> {
    if points.is_empty() {
        return Vec::new();
    }
    let newest = points[points.len() - 1].0;
    let span = (max - min).max(1e-9);
    points
        .iter()
        .map(|&(ts, v)| {
            let age = (newest - ts) as f32; // 0 at newest
            let x = w - (age / window_ms as f32) * w;
            let norm = ((v - min) / span).clamp(0.0, 1.0) as f32;
            let y = h - norm * h; // invert: max at top
            (x, y)
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_input_gives_no_points() {
        assert!(to_screen(&[], 1000, 100.0, 50.0, 0.0, 1.0).is_empty());
    }

    #[test]
    fn newest_point_maps_to_right_edge() {
        let pts = [(0i64, 0.0), (1000i64, 1.0)];
        let out = to_screen(&pts, 1000, 100.0, 50.0, 0.0, 1.0);
        assert!((out[1].0 - 100.0).abs() < 1e-3); // newest -> x = w
        assert!((out[0].0 - 0.0).abs() < 1e-3);    // oldest (age=window) -> x = 0
    }

    #[test]
    fn value_is_inverted_min_at_bottom_max_at_top() {
        let pts = [(0i64, 0.0), (0i64, 1.0)];
        let out = to_screen(&pts, 1000, 100.0, 50.0, 0.0, 1.0);
        assert!((out[0].1 - 50.0).abs() < 1e-3); // min -> y = h (bottom)
        assert!((out[1].1 - 0.0).abs() < 1e-3);  // max -> y = 0 (top)
    }

    #[test]
    fn value_is_clamped_to_min_max() {
        let pts = [(0i64, 5.0)]; // above max
        let out = to_screen(&pts, 1000, 100.0, 50.0, 0.0, 1.0);
        assert!((out[0].1 - 0.0).abs() < 1e-3); // clamped to max -> top
    }
}
```

- [ ] **Step 2: Wire the module**

Add to `rust-native/src/main.rs`: `mod chart;`

- [ ] **Step 3: Run tests**

Run: `cd rust-native && cargo test chart::`
Expected: 4 tests pass.

- [ ] **Step 4: Commit**

```bash
git add rust-native/src/chart.rs rust-native/src/main.rs
git commit -m "feat(rust-native): pure strip-chart geometry (samples -> pixels)"
```

---

### Task 5: Vello custom-canvas spike — render one static line (build + launch verify)

**Files:**
- Modify: `rust-native/src/chart.rs` (add a `paint` fn taking a Vello scene + screen points)
- Modify: `rust-native/src/main.rs` (mount one custom canvas element)

**Interfaces:**
- Consumes: `chart::to_screen`, `dioxus_native` custom-paint API + `vello`.
- Produces: `chart::paint_line(scene, points: &[(f32,f32)], color, width)` — strokes a `vello::kurbo::BezPath` polyline through `points` into the scene.

> **This is a spike:** the exact `dioxus-native` custom-paint callback signature is the main unknown. Follow the current `dioxus-native` custom-canvas / `CustomPaintCtx` example on docs.rs. The deliverable is: a static polyline visible in the window + the real signature captured in code (later tasks reuse `paint_line`). No unit test (GPU render).

- [ ] **Step 1: Add the Vello dependency (pinned)**

Run: `cd rust-native && cargo add vello` — then pin the exact version in `Cargo.toml` matching the version `dioxus-native`/`blitz-renderer-vello` already resolves (check `Cargo.lock` to avoid two `vello` versions). If they conflict, re-export Vello from `dioxus-native` if it provides it, and drop the direct dep.

- [ ] **Step 2: Add `paint_line`**

Append to `rust-native/src/chart.rs`:

```rust
use vello::kurbo::{BezPath, Point, Stroke};
use vello::peniko::Color;
use vello::Scene;

/// Stroke a polyline through screen-space `points` into a Vello scene.
pub fn paint_line(scene: &mut Scene, points: &[(f32, f32)], color: Color, width: f64) {
    if points.len() < 2 {
        return;
    }
    let mut path = BezPath::new();
    path.move_to(Point::new(points[0].0 as f64, points[0].1 as f64));
    for &(x, y) in &points[1..] {
        path.line_to(Point::new(x as f64, y as f64));
    }
    scene.stroke(&Stroke::new(width), vello::kurbo::Affine::IDENTITY, color, None, &path);
}
```
> If the pinned `vello` API differs (e.g. `Scene::stroke` argument order/types), adapt to the version's docs.rs — keep the function name and role identical.

- [ ] **Step 3: Mount a custom canvas rendering a static line**

In `rust-native/src/main.rs`, replace the placeholder body with a single custom-canvas element whose paint callback builds points from a hardcoded ramp and calls `chart::paint_line`. Follow the `dioxus-native` custom-canvas example for the exact element + `CustomPaintCtx` wiring; the paint body is:

```rust
let pts = chart::to_screen(
    &[(0, 0.0), (500, 1.0), (1000, 0.2)],
    1000, width, height, 0.0, 1.0,
);
chart::paint_line(scene, &pts, vello::peniko::Color::rgb8(0x38, 0xc5, 0xe0), 2.0);
```

- [ ] **Step 4: Build + launch verify**

Run: `cd rust-native && cargo run`
Expected: the window shows a cyan zig-zag line (the static ramp). Note the working `CustomPaintCtx` signature in a comment above the paint callback for later reuse.

- [ ] **Step 5: Commit**

```bash
git add rust-native/Cargo.toml rust-native/Cargo.lock rust-native/src/chart.rs rust-native/src/main.rs
git commit -m "feat(rust-native): Vello custom-canvas spike — static strip line"
```

---

### Task 6: Wire live data → charts + param table + HUD (build + launch verify)

**Files:**
- Create: `rust-native/src/ui.rs`
- Modify: `rust-native/src/main.rs` (mount `ui::app`, start feed + metrics loop)

**Interfaces:**
- Consumes: `feed::Feed`, `data::WindowBuffer`, `chart::{to_screen, paint_line}`, `app_lib::metrics::MetricsSampler`.
- Produces: `ui::app() -> Element` — the full v1 dashboard.

> Build + launch verify (RSX + Vello + timer — no unit test, per convention). This task assembles the pieces; the sub-steps below are the assembly + a launch check, not a TDD cycle.

- [ ] **Step 1: App state + feed/metrics timer**

Create `rust-native/src/ui.rs` holding: a `Signal`-backed state with one `WindowBuffer` per strip channel (keyed by channel id), the latest full `Sample`, and the latest `Metrics`. On mount, spawn a coroutine/interval (~16 ms tick) that: reads `Instant::now()` elapsed, calls `feed.due_upto(elapsed)`, pushes each due sample's strip values into the matching `WindowBuffer` (using `feed.strip_indices()`), stores latest values, and every ~500 ms calls `MetricsSampler::sample()`. Bump the reactive signal so the UI re-renders. `RIDE_DB` + `RIDE_SPEED` are read from env in `main.rs` and passed into `Feed::open`.

- [ ] **Step 2: RSX layout**

`ui::app` renders three regions in a dark theme matching `docs/reference/dashboard-target.md`:
- **Param table** — a scrollable list: one row per channel (`name`, formatted latest value, `unit`). HTML/CSS.
- **Chart grid** — one custom canvas per strip channel; its paint callback calls `chart::to_screen(buffer.points(), 60_000, w, h, min, max)` then `chart::paint_line(...)` with the cyan colour + a per-chart title (name + latest value).
- **HUD** — CPU% and RAM MB from the latest `Metrics`, plus FPS (frames rendered / elapsed).

- [ ] **Step 3: main.rs wiring**

`main.rs` reads `RIDE_DB` (env, else resolve `../data/ride.db` then `../data/ride_small.db`) and `RIDE_SPEED` (env, else 1.0), constructs the `Feed`, and calls `dioxus_native::launch` with `ui::app` (passing the feed/config in via context or a `once_cell`/`Signal`). Keep `mod data; mod feed; mod chart; mod ui;`.

- [ ] **Step 4: Build + launch verify**

Run:
```bash
cd rust-native
RIDE_DB=../data/ride_small.db RIDE_SPEED=1 cargo run
```
Expected: window shows the param table (values updating), 5 strip charts scrolling live over a 60 s window, and the HUD showing non-zero CPU%/RAM. No `msedgewebview2.exe` process. Let it run ~15 s to confirm charts scroll and evict. Close.

- [ ] **Step 5: Commit**

```bash
git add rust-native/src/ui.rs rust-native/src/main.rs
git commit -m "feat(rust-native): live dashboard — param table + strip charts + HUD"
```

---

### Task 7: Measure footprint + update README (docs)

**Files:**
- Create: `rust-native/README.md`
- Modify: `README.md` (root — "Measured footprint" table + intro table)

**Interfaces:** none (docs + measurement).

- [ ] **Step 1: Release build + warm run**

Run:
```bash
cd rust-native && cargo build --release
RIDE_DB=../data/ride.db RIDE_SPEED=1 ./target/release/rust-native   # (Windows: target\release\rust-native.exe)
```
Let it warm 30 s.

- [ ] **Step 2: Sample RAM + CPU over a 6 s window**

Using the same method as the existing table (single process — `rust-native`): record working-set MB and CPU% (`ΔTotalProcessorTime / (window_s × logical_cores) × 100`). Take two samples; note the range.

- [ ] **Step 3: Add the third row to the root README table**

In `README.md`, under "## Measured footprint", add a row to the table:

```markdown
| **Rust Dioxus-native** (Blitz/Vello, no WebView) | 1 | ~<measured> MB | ~<measured>% |
```
Add one sentence noting the native variant renders a **reduced dashboard** (param table + strip charts + HUD; no map/gauges yet), so its number is not yet a like-for-like full-dashboard comparison — and, if the RAM is indeed well below the Tauri figure, state that it confirms the WebView2 runtime (not the Rust code) was the memory driver.

- [ ] **Step 4: Write `rust-native/README.md`**

One short page: what it is (third variant, native Blitz/Vello, no WebView), v1 scope, how to run (`RIDE_DB`/`RIDE_SPEED`), how to test (`cargo test`), and the "experimental renderer — pinned versions" caveat.

- [ ] **Step 5: Commit**

```bash
git add README.md rust-native/README.md
git commit -m "docs(rust-native): measured footprint (3rd stack) + README"
```

---

## Self-Review

**Spec coverage:**
- Goal (native, beat WebView2 floor) → Tasks 1, 5, 6, 7. ✓
- Isolation (new crate, app_lib path dep, zero src-tauri changes) → Task 1 + Global Constraints. ✓
- In-process feed reusing replay/db → Task 3. ✓
- Windowed store (60 s port of ringBuffer) → Task 2. ✓
- Strip charts via CustomPaintCtx + Vello BezPath → Tasks 4 (geometry) + 5 (paint spike) + 6 (live). ✓
- Param table + HUD (HTML/CSS + metrics parity) → Task 6. ✓
- Fairness (60 s window, Pacer, MetricsSampler) → Global Constraints + Tasks 2/3/6. ✓
- Measurement + README 3rd row → Task 7. ✓
- Out-of-scope (map, gauges, grid, transport) → excluded, stated in Global Constraints. ✓
- Risks (experimental deps pinned, CustomPaintCtx unknown → spike, app_lib Tauri deps, fonts, WGPU) → Global Constraints + Task 5 spike; font/WGPU verified at launch in Tasks 1/6. ✓

**Placeholder scan:** The `~<measured>` values in Task 7 are measurement outputs (produced by running the step), not design placeholders. The Vello/custom-paint API notes in Task 5 are explicit spike instructions with a concrete fallback (follow docs.rs), not "implement later" hand-waves. No other placeholders.

**Type consistency:** `WindowBuffer` (Task 2) → used in `chart::to_screen(points)` (Task 4) via `buffer.points()`; `Feed::strip_indices`/`due_upto` (Task 3) → used in Task 6; `chart::paint_line` (Task 5) → used in Task 6. `Sample`/`ChannelMeta`/`Pacer`/`MetricsSampler` signatures match the verbatim `app_lib` block. Consistent.
