# Rust/Tauri Telemetry Backend

Rust/Tauri backend for the telemetry charting proof-of-concept. Replays a
`ride.db` SQLite database over a local WebSocket so a frontend (or any WS
client) can consume structured telemetry frames in real time or at a scaled
replay speed.

![Rust INU-MONITOR dashboard](../docs/screenshots/rust.png)

## How to run

1. Generate `data/ride.db` (from the repo root):
   ```
   python data/simulate.py
   ```
   Or point `RIDE_DB` at the bundled fixture instead (see env vars below).

2. Start the Tauri dev server:
   ```
   cd rust
   npm install
   npm run tauri dev
   ```

## Environment variables

| Variable       | Default                        | Description                                      |
|----------------|--------------------------------|--------------------------------------------------|
| `RIDE_WS_PORT` | `9001`                         | TCP port the WebSocket server listens on         |
| `RIDE_DB`      | `../../data/ride.db`           | Path to the SQLite ride database (relative to the Tauri binary cwd `rust/src-tauri/`) |
| `RIDE_SPEED`   | `1.0`                          | Replay multiplier (e.g. `10.0` = 10x fast-forward) |

## WebSocket wire protocol

Connect to `ws://127.0.0.1:9001` (or the port set by `RIDE_WS_PORT`).

### Message sequence

1. **meta** — sent once immediately after connection:
   ```json
   {
     "type": "meta",
     "channels": [{ "name": "...", "unit": "...", "display_order": 0, ... }],
     "enum_values": [{ "channel_name": "...", "value": 1, "label": "..." }],
     "rate_hz": 30
   }
   ```
   `channels` is sorted by `display_order`; all subsequent `frame.values` are
   index-aligned to this array.

2. **frame** — one per sample, paced to replay speed:
   ```json
   { "type": "frame", "ts_ms": 1234, "emit_unix_ms": 1700000000000, "values": [1.5, 0.0, ...] }
   ```
   - `ts_ms` — ride-relative timestamp in milliseconds (monotonically increasing).
   - `emit_unix_ms` — wall-clock Unix time (ms) when the server emitted the
     frame; `Date.now() - emit_unix_ms` gives end-to-end latency.
   - `values` — 30 floats, index-aligned to `meta.channels`.

3. **metrics** — emitted approximately once per ride-second (replay-time based,
   so it fires at the correct cadence regardless of replay speed):
   ```json
   { "type": "metrics", "cpu_pct": 3.2, "ram_mb": 42.5 }
   ```

## How to test

```
cd rust/src-tauri
cargo test
```

All unit tests (frame serialization, Pacer arithmetic, DB loading, metrics
sampler) and the integration test (`ws_integration`) run together.

## Run the dashboard

The dashboard UI is a Tauri 2 + React application that connects to the
backend WebSocket server to display real-time telemetry data.

### Prerequisites

Generate a ride database:
```
python data/simulate.py
```

Or use the committed fixture for a quick run:
```
data/ride_small.db
```

### Launch the dashboard

From the repository root or the `rust/` directory:
```
RIDE_DB=../../data/ride_small.db RIDE_SPEED=5 npm run tauri dev
```

**Note:** The `RIDE_DB` path is resolved relative to the Tauri binary working
directory (`rust/src-tauri/`), so `../../data/ride_small.db` refers to the
fixture at the repository root.

### Frontend configuration

The UI connects to the WebSocket server at `ws://127.0.0.1:9001` (default).
Override by setting the `VITE_WS_URL` environment variable:
```
VITE_WS_URL=ws://your-host:9001 npm run tauri dev
```

### Dashboard layout

The dashboard layout design is documented in
[`docs/reference/dashboard-target.md`](../docs/reference/dashboard-target.md).

### Testing

**Frontend tests:**
```
cd rust
npm test
```

**Backend tests:**
```
cd rust/src-tauri
cargo test
```

## Tests

The test suite consists of unit tests, backend tests, and end-to-end visual regression tests.

### Unit and component tests

```
npm test
```

Runs vitest unit tests covering pure logic and jsdom component smoke tests. All tests are deterministic and do not require external services.

### Backend tests

```
cd src-tauri && cargo test
```

Runs the Rust backend test suite, including frame serialization, Pacer arithmetic, database loading, metrics sampler, and WebSocket integration tests.

### End-to-end tests

```
npm run e2e
```

Runs Playwright end-to-end tests with visual regression. Uses `?mock=1` for deterministic in-app mock telemetry (no backend needed). Automatically boots the Vite dev server. Tests verify both functional behavior and screenshot consistency against committed baselines.

**Screenshot baselines**

Baselines are OS- and browser-specific. The committed baselines target Chromium on Windows (`*-chromium-win32.png`). To refresh baselines after an intentional UI change:

```
npm run e2e:update
```

This updates the baseline screenshots. Always commit these changes along with your UI modifications.

> **Stale-server footgun:** Before regenerating baselines, stop any running `npm run dev`/`tauri dev` server and clear the Vite cache (`rm -rf node_modules/.vite`) — Playwright's `reuseExistingServer` will otherwise screenshot stale code/cache.
