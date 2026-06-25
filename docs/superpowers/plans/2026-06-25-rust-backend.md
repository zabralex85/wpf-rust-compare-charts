# Rust Backend (Tauri) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Rust/Tauri backend that reads the shared `ride.db`, replays its samples in time order at a controllable speed, and streams them (plus channel metadata and process metrics) to the frontend over a local WebSocket.

**Architecture:** A Tauri 2 app under `rust/`. The Rust crate (`src-tauri/`) is split into focused modules: `db` (rusqlite reader for channel metadata + samples), `frame` (wire model + serde JSON), `replay` (deterministic pacing with an injected clock), `metrics` (sysinfo process sampler), and `server` (a tokio WebSocket server spawned from Tauri's setup hook). Pure logic is unit-tested against the committed `ride_small.db` fixture; the WebSocket server has an integration test using a real client on an ephemeral port.

**Tech Stack:** Rust (edition 2021), Tauri 2, rusqlite (bundled SQLite), serde/serde_json, tokio, tokio-tungstenite, futures-util, sysinfo. Frontend scaffold: React + TypeScript + Vite (wired in Plan 3; here only enough to run).

## Global Constraints

- The DB contract is produced by `data/` (Plan 1) and is read-only here. Tables: `channels(id, name, column_name, unit, type, min, max, widget, display_order, addr)`, `enum_values(channel_id, code, label, severity)`, wide `samples(ts INTEGER PRIMARY KEY, <one col per channel in display order>)`, `ride_meta(start_time, duration_s, rate_hz, channel_count)`.
- `ts` is integer **milliseconds** from ride start, monotonic, step `1000/rate_hz`.
- Fixture for all tests: `../../data/ride_small.db` (relative to `rust/src-tauri/`) — 100 rows, 30 channels, 10 Hz, contains ≥1 `inu_mode2=1` enum event.
- WebSocket binds `127.0.0.1` on a configurable port (default **9001**); env var `RIDE_WS_PORT` overrides. DB path default `../data/ride.db` relative to `rust/`; env var `RIDE_DB` overrides.
- Wire format = **JSON** text frames. Two message kinds, discriminated by a `type` field: `"meta"` (sent once on connect) and `"frame"` (streamed).
- Replay pacing logic must be **deterministic and clock-injected** — no real sleeps inside unit tests.
- Frames carry `emit_unix_ms` (wall-clock at emit) so the frontend can compute end-to-end latency.

---

### Task 1: Scaffold Tauri 2 + React/TS app

**Files:**
- Create: `rust/package.json`, `rust/index.html`, `rust/vite.config.ts`, `rust/tsconfig.json`, `rust/src/main.tsx`, `rust/src/App.tsx` (minimal)
- Create: `rust/src-tauri/Cargo.toml`, `rust/src-tauri/tauri.conf.json`, `rust/src-tauri/build.rs`, `rust/src-tauri/src/main.rs`, `rust/src-tauri/src/lib.rs`
- Create: `rust/.gitignore`

**Interfaces:**
- Produces: a buildable workspace — `cargo test` (in `src-tauri/`) and `npm run build` (in `rust/`) both succeed. `lib.rs` exposes `pub fn run()` called by `main.rs` (standard Tauri 2 layout).

This task is scaffolding; verification is "it builds", not a unit test.

- [ ] **Step 1: Scaffold with the Tauri CLI**

Run (from repo root):
```bash
cd rust 2>/dev/null || mkdir rust && cd rust
npm create tauri-app@latest . -- --template react-ts --manager npm --yes
```
If the interactive prompt cannot be bypassed, instead create the app in a temp dir and move it: `npm create tauri-app@latest tmpapp -- --template react-ts --manager npm --yes && mv tmpapp/* tmpapp/.* . 2>/dev/null; rmdir tmpapp`.

Expected: `rust/` now contains `package.json`, `src/`, `src-tauri/` with a default Tauri 2 React-TS app.

- [ ] **Step 2: Pin backend dependencies**

Edit `rust/src-tauri/Cargo.toml` so `[dependencies]` includes (keep the generated `tauri`, `serde`, `serde_json` entries; add the rest):
```toml
[dependencies]
tauri = { version = "2", features = [] }
serde = { version = "1", features = ["derive"] }
serde_json = "1"
rusqlite = { version = "0.31", features = ["bundled"] }
tokio = { version = "1", features = ["rt-multi-thread", "macros", "sync", "time", "net"] }
tokio-tungstenite = "0.21"
futures-util = "0.3"
sysinfo = "0.30"

[dev-dependencies]
tokio = { version = "1", features = ["rt-multi-thread", "macros", "sync", "time", "net"] }
```

- [ ] **Step 3: Verify both halves build**

Run:
```bash
cd rust && npm install && npm run build
cd src-tauri && cargo test
```
Expected: `npm run build` produces `dist/`; `cargo test` compiles and runs 0 tests (`test result: ok. 0 passed`).

- [ ] **Step 4: Add rust/.gitignore**

```
# rust/.gitignore
node_modules/
dist/
src-tauri/target/
```

- [ ] **Step 5: Commit**

```bash
git add rust/
git commit -m "feat(rust): scaffold Tauri 2 + React/TS app"
```

---

### Task 2: Channel metadata reader

**Files:**
- Create: `rust/src-tauri/src/db.rs`
- Modify: `rust/src-tauri/src/lib.rs` (add `pub mod db;`)
- Test: inline `#[cfg(test)]` module in `db.rs`

**Interfaces:**
- Produces:
  - `pub struct ChannelMeta { pub id: i64, pub name: String, pub column_name: String, pub unit: String, pub type_: String, pub min: f64, pub max: f64, pub widget: String, pub display_order: i64, pub addr: String }` (derive `Debug, Clone, serde::Serialize`)
  - `pub struct EnumValue { pub channel_id: i64, pub code: i64, pub label: String, pub severity: String }` (derive `Debug, Clone, serde::Serialize`)
  - `pub fn load_channels(conn: &rusqlite::Connection) -> rusqlite::Result<Vec<ChannelMeta>>` — ordered by `display_order`
  - `pub fn load_enum_values(conn: &rusqlite::Connection) -> rusqlite::Result<Vec<EnumValue>>`

- [ ] **Step 1: Write the failing test**

Add to the bottom of `db.rs`:
```rust
#[cfg(test)]
mod tests {
    use super::*;
    use rusqlite::Connection;

    fn fixture() -> Connection {
        Connection::open("../../data/ride_small.db").expect("open fixture")
    }

    #[test]
    fn loads_thirty_channels_in_display_order() {
        let conn = fixture();
        let chans = load_channels(&conn).unwrap();
        assert_eq!(chans.len(), 30);
        let orders: Vec<i64> = chans.iter().map(|c| c.display_order).collect();
        let mut sorted = orders.clone();
        sorted.sort();
        assert_eq!(orders, sorted, "rows must come back in display order");
        assert_eq!(chans[0].id, 1);
    }

    #[test]
    fn loads_inu_mode2_enum_values() {
        let conn = fixture();
        let evs = load_enum_values(&conn).unwrap();
        let labels: Vec<&str> = evs.iter().map(|e| e.label.as_str()).collect();
        assert!(labels.contains(&"Normal"));
        assert!(labels.contains(&"Critical"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `rust/src-tauri`): `cargo test db::`
Expected: FAIL to compile — `cannot find function load_channels`.

- [ ] **Step 3: Write minimal implementation**

At the top of `db.rs`:
```rust
use rusqlite::{Connection, Result};
use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
pub struct ChannelMeta {
    pub id: i64,
    pub name: String,
    pub column_name: String,
    pub unit: String,
    #[serde(rename = "type")]
    pub type_: String,
    pub min: f64,
    pub max: f64,
    pub widget: String,
    pub display_order: i64,
    pub addr: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct EnumValue {
    pub channel_id: i64,
    pub code: i64,
    pub label: String,
    pub severity: String,
}

pub fn load_channels(conn: &Connection) -> Result<Vec<ChannelMeta>> {
    let mut stmt = conn.prepare(
        "SELECT id, name, column_name, unit, type, min, max, widget, display_order, addr \
         FROM channels ORDER BY display_order",
    )?;
    let rows = stmt.query_map([], |r| {
        Ok(ChannelMeta {
            id: r.get(0)?,
            name: r.get(1)?,
            column_name: r.get(2)?,
            unit: r.get(3)?,
            type_: r.get(4)?,
            min: r.get(5)?,
            max: r.get(6)?,
            widget: r.get(7)?,
            display_order: r.get(8)?,
            addr: r.get(9)?,
        })
    })?;
    rows.collect()
}

pub fn load_enum_values(conn: &Connection) -> Result<Vec<EnumValue>> {
    let mut stmt = conn.prepare(
        "SELECT channel_id, code, label, severity FROM enum_values ORDER BY channel_id, code",
    )?;
    let rows = stmt.query_map([], |r| {
        Ok(EnumValue {
            channel_id: r.get(0)?,
            code: r.get(1)?,
            label: r.get(2)?,
            severity: r.get(3)?,
        })
    })?;
    rows.collect()
}
```
Add `pub mod db;` to `lib.rs`.

- [ ] **Step 4: Run test to verify it passes**

Run (from `rust/src-tauri`): `cargo test db::`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/db.rs rust/src-tauri/src/lib.rs
git commit -m "feat(rust): channel + enum metadata reader (rusqlite)"
```

---

### Task 3: Sample reader

**Files:**
- Modify: `rust/src-tauri/src/db.rs` (add sample reading)
- Test: extend the `#[cfg(test)]` module

**Interfaces:**
- Consumes: `ChannelMeta` (for column order)
- Produces:
  - `pub struct Sample { pub ts_ms: i64, pub values: Vec<f64> }` (derive `Debug, Clone`)
  - `pub fn load_samples(conn: &rusqlite::Connection, channels: &[ChannelMeta]) -> rusqlite::Result<Vec<Sample>>` — one `Sample` per row, ordered by `ts`, `values` in `channels` order; every value read as `f64` (enum/int columns coerce to f64).

- [ ] **Step 1: Write the failing test**

Add to the test module:
```rust
    #[test]
    fn loads_samples_in_ts_order_with_one_value_per_channel() {
        let conn = fixture();
        let chans = load_channels(&conn).unwrap();
        let samples = load_samples(&conn, &chans).unwrap();
        assert_eq!(samples.len(), 100);
        assert_eq!(samples[0].ts_ms, 0);
        assert_eq!(samples[1].ts_ms, 100);
        assert!(samples.iter().all(|s| s.values.len() == chans.len()));
        // monotonic ts
        assert!(samples.windows(2).all(|w| w[1].ts_ms > w[0].ts_ms));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cargo test db::loads_samples`
Expected: FAIL — `cannot find function load_samples`.

- [ ] **Step 3: Write minimal implementation**

Add to `db.rs`:
```rust
#[derive(Debug, Clone)]
pub struct Sample {
    pub ts_ms: i64,
    pub values: Vec<f64>,
}

pub fn load_samples(conn: &Connection, channels: &[ChannelMeta]) -> Result<Vec<Sample>> {
    let cols: Vec<String> = channels.iter().map(|c| c.column_name.clone()).collect();
    let select = cols.join(", ");
    let sql = format!("SELECT ts, {} FROM samples ORDER BY ts", select);
    let mut stmt = conn.prepare(&sql)?;
    let n = channels.len();
    let rows = stmt.query_map([], |r| {
        let ts_ms: i64 = r.get(0)?;
        let mut values = Vec::with_capacity(n);
        for i in 0..n {
            // column index i+1; coerce any numeric storage to f64
            let v: f64 = r.get::<_, f64>(i + 1).or_else(|_| {
                r.get::<_, i64>(i + 1).map(|x| x as f64)
            })?;
            values.push(v);
        }
        Ok(Sample { ts_ms, values })
    })?;
    rows.collect()
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cargo test db::`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/db.rs
git commit -m "feat(rust): wide samples reader coercing values to f64"
```

---

### Task 4: Wire frame model + JSON

**Files:**
- Create: `rust/src-tauri/src/frame.rs`
- Modify: `rust/src-tauri/src/lib.rs` (add `pub mod frame;`)
- Test: inline `#[cfg(test)]` in `frame.rs`

**Interfaces:**
- Consumes: `ChannelMeta`, `EnumValue` (from `db`)
- Produces:
  - `pub struct MetaMessage { pub type_: "meta", pub channels: Vec<ChannelMeta>, pub enum_values: Vec<EnumValue>, pub rate_hz: i64 }` — serialized with `type` field = `"meta"`
  - `pub struct FrameMessage { pub type_: "frame", pub ts_ms: i64, pub emit_unix_ms: i64, pub values: Vec<f64> }` — `type` field = `"frame"`
  - both derive `Serialize`; `type` produced via `#[serde(tag = ...)]`-style fixed field (see code)

- [ ] **Step 1: Write the failing test**

```rust
// frame.rs tests
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn frame_serializes_with_type_tag() {
        let f = FrameMessage { ts_ms: 100, emit_unix_ms: 1_700_000_000_000, values: vec![1.5, 2.0] };
        let json = serde_json::to_string(&f).unwrap();
        let v: serde_json::Value = serde_json::from_str(&json).unwrap();
        assert_eq!(v["type"], "frame");
        assert_eq!(v["ts_ms"], 100);
        assert_eq!(v["emit_unix_ms"], 1_700_000_000_000i64);
        assert_eq!(v["values"][0], 1.5);
    }

    #[test]
    fn meta_serializes_with_type_tag() {
        let m = MetaMessage { channels: vec![], enum_values: vec![], rate_hz: 10 };
        let v: serde_json::Value = serde_json::from_str(&serde_json::to_string(&m).unwrap()).unwrap();
        assert_eq!(v["type"], "meta");
        assert_eq!(v["rate_hz"], 10);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cargo test frame::`
Expected: FAIL — `cannot find type FrameMessage`.

- [ ] **Step 3: Write minimal implementation**

```rust
// frame.rs
use serde::Serialize;
use crate::db::{ChannelMeta, EnumValue};

#[derive(Debug, Clone, Serialize)]
pub struct MetaMessage {
    #[serde(rename = "type")]
    pub type_: &'static str,
    pub channels: Vec<ChannelMeta>,
    pub enum_values: Vec<EnumValue>,
    pub rate_hz: i64,
}

impl MetaMessage {
    pub fn new(channels: Vec<ChannelMeta>, enum_values: Vec<EnumValue>, rate_hz: i64) -> Self {
        Self { type_: "meta", channels, enum_values, rate_hz }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct FrameMessage {
    #[serde(rename = "type")]
    pub type_: &'static str,
    pub ts_ms: i64,
    pub emit_unix_ms: i64,
    pub values: Vec<f64>,
}

impl FrameMessage {
    pub fn new(ts_ms: i64, emit_unix_ms: i64, values: Vec<f64>) -> Self {
        Self { type_: "frame", ts_ms, emit_unix_ms, values }
    }
}
```
Update the tests to use the constructors (`MetaMessage::new(...)`, `FrameMessage::new(...)`) since `type_` is set internally. Add `pub mod frame;` to `lib.rs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `cargo test frame::`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/frame.rs rust/src-tauri/src/lib.rs
git commit -m "feat(rust): JSON wire frame + meta message models"
```

---

### Task 5: Deterministic replay pacing

**Files:**
- Create: `rust/src-tauri/src/replay.rs`
- Modify: `rust/src-tauri/src/lib.rs` (add `pub mod replay;`)
- Test: inline `#[cfg(test)]`

**Interfaces:**
- Produces:
  - `pub struct Pacer { start_ms: i64, speed: f64 }`
  - `pub fn new(speed: f64) -> Pacer` — `speed` is the replay multiplier (1.0 = realtime)
  - `pub fn due_offset_ms(&self, sample_ts_ms: i64) -> i64` — wall-clock offset from replay start at which `sample_ts_ms` should be emitted = `(sample_ts_ms as f64 / speed) as i64`
  - `pub fn wait_ms(&self, sample_ts_ms: i64, elapsed_ms: i64) -> i64` — how long to sleep given elapsed wall time since start; `max(0, due_offset - elapsed)`

Pure arithmetic; the WS server (Task 7) supplies real elapsed time and does the actual sleeping.

- [ ] **Step 1: Write the failing test**

```rust
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn realtime_due_offset_equals_ts() {
        let p = Pacer::new(1.0);
        assert_eq!(p.due_offset_ms(0), 0);
        assert_eq!(p.due_offset_ms(100), 100);
        assert_eq!(p.due_offset_ms(43_200_000), 43_200_000);
    }

    #[test]
    fn fast_forward_compresses_time() {
        let p = Pacer::new(10.0);
        assert_eq!(p.due_offset_ms(1000), 100);
    }

    #[test]
    fn wait_never_negative_and_accounts_for_elapsed() {
        let p = Pacer::new(1.0);
        assert_eq!(p.wait_ms(100, 0), 100);
        assert_eq!(p.wait_ms(100, 40), 60);
        assert_eq!(p.wait_ms(100, 500), 0); // behind schedule -> no wait
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cargo test replay::`
Expected: FAIL — `cannot find type Pacer`.

- [ ] **Step 3: Write minimal implementation**

```rust
// replay.rs
pub struct Pacer {
    speed: f64,
}

impl Pacer {
    pub fn new(speed: f64) -> Self {
        let speed = if speed <= 0.0 { 1.0 } else { speed };
        Self { speed }
    }

    pub fn due_offset_ms(&self, sample_ts_ms: i64) -> i64 {
        (sample_ts_ms as f64 / self.speed) as i64
    }

    pub fn wait_ms(&self, sample_ts_ms: i64, elapsed_ms: i64) -> i64 {
        (self.due_offset_ms(sample_ts_ms) - elapsed_ms).max(0)
    }
}
```
Add `pub mod replay;` to `lib.rs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `cargo test replay::`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/replay.rs rust/src-tauri/src/lib.rs
git commit -m "feat(rust): deterministic replay pacer"
```

---

### Task 6: Process metrics sampler

**Files:**
- Create: `rust/src-tauri/src/metrics.rs`
- Modify: `rust/src-tauri/src/lib.rs` (add `pub mod metrics;`)
- Test: inline `#[cfg(test)]`

**Interfaces:**
- Produces:
  - `pub struct Metrics { pub cpu_pct: f32, pub ram_mb: f64 }` (derive `Debug, Clone, Serialize`)
  - `pub struct MetricsSampler { sys: sysinfo::System, pid: sysinfo::Pid }`
  - `pub fn new() -> MetricsSampler` — captures the current process pid
  - `pub fn sample(&mut self) -> Metrics` — refreshes this process and returns its CPU% and resident RAM in MB

- [ ] **Step 1: Write the failing test**

```rust
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sample_returns_finite_nonnegative_ram() {
        let mut s = MetricsSampler::new();
        let _ = s.sample();              // first read primes cpu measurement
        let m = s.sample();
        assert!(m.ram_mb > 0.0, "resident RAM should be positive");
        assert!(m.cpu_pct >= 0.0);
        assert!(m.cpu_pct.is_finite());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cargo test metrics::`
Expected: FAIL — `cannot find type MetricsSampler`.

- [ ] **Step 3: Write minimal implementation**

```rust
// metrics.rs
use serde::Serialize;
use sysinfo::{Pid, ProcessRefreshKind, RefreshKind, System};

#[derive(Debug, Clone, Serialize)]
pub struct Metrics {
    pub cpu_pct: f32,
    pub ram_mb: f64,
}

pub struct MetricsSampler {
    sys: System,
    pid: Pid,
}

impl MetricsSampler {
    pub fn new() -> Self {
        let pid = Pid::from_u32(std::process::id());
        let sys = System::new_with_specifics(
            RefreshKind::new().with_processes(ProcessRefreshKind::everything()),
        );
        Self { sys, pid }
    }

    pub fn sample(&mut self) -> Metrics {
        self.sys
            .refresh_process_specifics(self.pid, ProcessRefreshKind::everything());
        match self.sys.process(self.pid) {
            Some(p) => Metrics {
                cpu_pct: p.cpu_usage(),
                ram_mb: p.memory() as f64 / 1_048_576.0,
            },
            None => Metrics { cpu_pct: 0.0, ram_mb: 0.0 },
        }
    }
}
```
Add `pub mod metrics;` to `lib.rs`. (Note: `sysinfo` 0.30 reports `memory()` in bytes.)

- [ ] **Step 4: Run test to verify it passes**

Run: `cargo test metrics::`
Expected: PASS (1 test). If CPU is 0.0 on the first primed read on some platforms, the test only asserts `>= 0.0`, so it still passes.

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/metrics.rs rust/src-tauri/src/lib.rs
git commit -m "feat(rust): per-process CPU/RAM metrics sampler"
```

---

### Task 7: WebSocket server + Tauri wiring

**Files:**
- Create: `rust/src-tauri/src/server.rs`
- Modify: `rust/src-tauri/src/lib.rs` (add `pub mod server;`, spawn server in Tauri setup)
- Test: `rust/src-tauri/tests/ws_integration.rs`

**Interfaces:**
- Consumes: `db`, `frame`, `replay`, `metrics`
- Produces:
  - `pub struct ServerConfig { pub db_path: String, pub port: u16, pub speed: f64 }`
  - `pub async fn serve(config: ServerConfig) -> std::io::Result<()>` — binds `127.0.0.1:port`; on each client connection: send one `MetaMessage`, then stream `FrameMessage`s paced by `Pacer`, interleaving a metrics frame roughly once per second.
  - `pub async fn serve_on(listener: tokio::net::TcpListener, config: ServerConfig)` — same logic on an already-bound listener (lets tests pick an ephemeral port).

- [ ] **Step 1: Write the failing integration test**

```rust
// rust/src-tauri/tests/ws_integration.rs
use futures_util::StreamExt;
use tokio_tungstenite::connect_async;

#[tokio::test]
async fn client_receives_meta_then_frames() {
    // ephemeral port
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    let cfg = app_lib::server::ServerConfig {
        db_path: "../../data/ride_small.db".into(),
        port: addr.port(),
        speed: 1000.0, // fast-forward so the 10s ride streams quickly
    };
    tokio::spawn(async move { app_lib::server::serve_on(listener, cfg).await });

    let url = format!("ws://{}", addr);
    let (mut ws, _) = connect_async(&url).await.expect("connect");

    // first message must be meta
    let first = ws.next().await.unwrap().unwrap();
    let v: serde_json::Value = serde_json::from_str(first.to_text().unwrap()).unwrap();
    assert_eq!(v["type"], "meta");
    assert_eq!(v["channels"].as_array().unwrap().len(), 30);

    // then at least a few frames, ts increasing
    let mut last_ts = -1i64;
    let mut frames = 0;
    while frames < 5 {
        let msg = ws.next().await.unwrap().unwrap();
        let v: serde_json::Value = serde_json::from_str(msg.to_text().unwrap()).unwrap();
        if v["type"] == "frame" {
            let ts = v["ts_ms"].as_i64().unwrap();
            assert!(ts > last_ts);
            last_ts = ts;
            frames += 1;
        }
    }
}
```
Note: the test refers to the crate as `app_lib`. Ensure `src-tauri/Cargo.toml` sets `[lib] name = "app_lib"` (the Tauri 2 template usually names it `<app>_lib`; adjust the test's crate name to match the actual `[lib] name` if different, and record the chosen name in your report).

- [ ] **Step 2: Run test to verify it fails**

Run (from `rust/src-tauri`): `cargo test --test ws_integration`
Expected: FAIL — `module server not found` / `serve_on` undefined.

- [ ] **Step 3: Write minimal implementation**

```rust
// server.rs
use std::time::{SystemTime, UNIX_EPOCH, Instant};
use futures_util::SinkExt;
use tokio::net::TcpListener;
use tokio_tungstenite::tungstenite::Message;
use rusqlite::Connection;

use crate::db::{load_channels, load_enum_values, load_samples};
use crate::frame::{FrameMessage, MetaMessage};
use crate::metrics::MetricsSampler;
use crate::replay::Pacer;

#[derive(Clone)]
pub struct ServerConfig {
    pub db_path: String,
    pub port: u16,
    pub speed: f64,
}

fn now_unix_ms() -> i64 {
    SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_millis() as i64
}

pub async fn serve(config: ServerConfig) -> std::io::Result<()> {
    let listener = TcpListener::bind(("127.0.0.1", config.port)).await?;
    serve_on(listener, config).await;
    Ok(())
}

pub async fn serve_on(listener: TcpListener, config: ServerConfig) {
    loop {
        let (stream, _) = match listener.accept().await {
            Ok(x) => x,
            Err(_) => continue,
        };
        let cfg = config.clone();
        tokio::spawn(async move {
            let _ = handle_client(stream, cfg).await;
        });
    }
}

async fn handle_client(stream: tokio::net::TcpStream, cfg: ServerConfig) -> anyhow::Result<()> {
    let mut ws = tokio_tungstenite::accept_async(stream).await?;

    // Load DB on a blocking thread (rusqlite is sync).
    let db_path = cfg.db_path.clone();
    let (meta_json, samples, _rate) = tokio::task::spawn_blocking(move || {
        let conn = Connection::open(&db_path)?;
        let channels = load_channels(&conn)?;
        let enums = load_enum_values(&conn)?;
        let rate: i64 = conn.query_row("SELECT rate_hz FROM ride_meta", [], |r| r.get(0))?;
        let samples = load_samples(&conn, &channels)?;
        let meta = MetaMessage::new(channels, enums, rate);
        Ok::<_, rusqlite::Error>((serde_json::to_string(&meta).unwrap(), samples, rate))
    })
    .await??;

    ws.send(Message::Text(meta_json)).await?;

    let pacer = Pacer::new(cfg.speed);
    let mut sampler = MetricsSampler::new();
    let start = Instant::now();
    let mut last_metrics = Instant::now();

    for s in samples {
        let elapsed = start.elapsed().as_millis() as i64;
        let wait = pacer.wait_ms(s.ts_ms, elapsed);
        if wait > 0 {
            tokio::time::sleep(std::time::Duration::from_millis(wait as u64)).await;
        }
        let frame = FrameMessage::new(s.ts_ms, now_unix_ms(), s.values);
        ws.send(Message::Text(serde_json::to_string(&frame)?)).await?;

        if last_metrics.elapsed().as_millis() >= 1000 {
            last_metrics = Instant::now();
            let m = sampler.sample();
            let mj = serde_json::json!({ "type": "metrics", "cpu_pct": m.cpu_pct, "ram_mb": m.ram_mb });
            ws.send(Message::Text(mj.to_string())).await?;
        }
    }
    Ok(())
}
```
Add `anyhow = "1"` to `[dependencies]` in `Cargo.toml`. Add `pub mod server;` to `lib.rs`.

- [ ] **Step 4: Wire the server into Tauri setup**

In `lib.rs`, inside the `run()` builder's `.setup(|app| { ... })` (add `.setup` if absent), spawn the server using Tauri's async runtime:
```rust
.setup(|_app| {
    let port: u16 = std::env::var("RIDE_WS_PORT").ok()
        .and_then(|s| s.parse().ok()).unwrap_or(9001);
    let db_path = std::env::var("RIDE_DB").unwrap_or_else(|_| "../data/ride.db".into());
    let speed: f64 = std::env::var("RIDE_SPEED").ok()
        .and_then(|s| s.parse().ok()).unwrap_or(1.0);
    tauri::async_runtime::spawn(async move {
        let cfg = crate::server::ServerConfig { db_path, port, speed };
        if let Err(e) = crate::server::serve(cfg).await {
            eprintln!("ws server error: {e}");
        }
    });
    Ok(())
})
```

- [ ] **Step 5: Run test to verify it passes**

Run (from `rust/src-tauri`): `cargo test --test ws_integration -- --nocapture`
Expected: PASS — meta received with 30 channels, then ≥5 increasing-ts frames.

- [ ] **Step 6: Full backend test sweep + commit**

Run: `cargo test`
Expected: all unit tests + the integration test pass.
```bash
git add rust/src-tauri/
git commit -m "feat(rust): websocket replay server wired into Tauri setup"
```

---

## Self-Review

**Spec coverage:**
- Replay from DB at speed → Tasks 3,5,7 ✓
- WebSocket transport, JSON frames, meta-on-connect → Tasks 4,7 ✓
- Channel metadata from DB drives wire `meta` → Tasks 2,4,7 ✓
- `emit_unix_ms` for end-to-end latency → Task 4 ✓
- Process CPU/RAM metrics (sysinfo) → Tasks 6,7 ✓
- Configurable port/db/speed via env → Task 7 ✓
- Deterministic, sleep-free pacing tests → Task 5 ✓
- Reads the Plan-1 DB contract (`column_name`, wide `samples`, `ride_meta.rate_hz`, `enum_values`) → Tasks 2,3,7 ✓

**Placeholder scan:** No TBD/TODO; every step carries real code/commands. The one variable is the Tauri `[lib] name` (template-dependent) — Task 7 Step 1 explicitly instructs verifying it and adjusting the test's `app_lib` references. ✓

**Type consistency:** `ChannelMeta`/`EnumValue`/`Sample` defined in Task 2-3 and consumed in 4,7. `MetaMessage::new`/`FrameMessage::new` constructors defined in Task 4, used in Task 7. `Pacer::new`/`wait_ms` from Task 5 used in Task 7. `MetricsSampler::new`/`sample` from Task 6 used in Task 7. `serve_on(listener, cfg)` defined in Task 7 and called by the integration test. ✓

> **Note for Plan 3 (frontend):** connect to `ws://127.0.0.1:9001`. First text message is `{"type":"meta", channels:[...], enum_values:[...], rate_hz}`. Then `{"type":"frame", ts_ms, emit_unix_ms, values:[...]}` where `values` align by index to `channels` (sorted by `display_order`). Occasional `{"type":"metrics", cpu_pct, ram_mb}`. Compute latency = `Date.now() - emit_unix_ms`.
