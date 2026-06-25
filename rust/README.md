# Rust/Tauri Telemetry Backend

Rust/Tauri backend for the telemetry charting proof-of-concept. Replays a
`ride.db` SQLite database over a local WebSocket so a frontend (or any WS
client) can consume structured telemetry frames in real time or at a scaled
replay speed.

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
