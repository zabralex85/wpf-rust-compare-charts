# rust-native — Dioxus-native (Blitz + Vello) variant

A **third** implementation of the telemetry dashboard, rendered **natively** with
[`dioxus-native`](https://crates.io/crates/dioxus-native) — Blitz for HTML/CSS
layout and Vello/WGPU for GPU 2D graphics. **No WebView.** It exists to test
whether dropping the Chromium/WebView2 runtime beats the Tauri footprint.

Result (see the root README "Measured footprint" table): it does beat Tauri —
single process, ~100 MB less RAM — but still loses to the .NET/Skia app (~2×),
and the WGPU + Vello + `system-fonts` baseline is a real ~530 MB floor. A native
GPU stack is **not** automatically lighter than Chromium.

## Scope (v1)

- Grouped parameter table (mirrors the Tauri `groups.ts` grouping)
- One scrolling strip chart per strip channel (60 s window), drawn with a
  single shared `vello::Renderer` into per-canvas WGPU textures
- Perf HUD: CPU% / RAM MB / FPS

Not yet: GPS map (MVT→Vello), radial gauges, interactive drag-grid, transport
controls. The footprint number will grow as these land.

## Design

- **No shared UI code** with the Tauri/​.NET apps (each stack is idiomatic), but
  it **reuses the Rust backend logic** via a path dependency on `app_lib`
  (`../rust/src-tauri`): `db` (SQLite reader), `replay::Pacer` (deterministic
  pacing), `metrics` (sysinfo sampler). Replay runs **in-process** — no
  WebSocket (mirrors the .NET app's idiom).
- `data.rs` windowed strip buffer + `feed.rs` in-process pacer + `chart.rs`
  geometry are pure and unit-tested; the Vello paint + RSX + window are
  build-verified + launch-confirmed (repo convention).

## Run

```bash
cd rust-native
RIDE_DB=../data/ride_small.db RIDE_SPEED=1 cargo run --release
```
`RIDE_DB` (env, else `../data/ride.db` then `../data/ride_small.db`) and
`RIDE_SPEED` (env, else `1.0`).

## Test

```bash
cargo test        # data.rs windowing, feed.rs pacing, chart.rs geometry
```

## Build note (Windows / rustc 1.95)

**Serial builds only.** Parallel `cargo build` crashes rustc with
`STATUS_STACK_BUFFER_OVERRUN (0xc0000409)` while compiling the vello/blitz/wgpu
stack on this Windows/MSVC toolchain. `.cargo/config.toml` pins `jobs = 1`, so
plain `cargo build`/`test`/`run` are serial (and slower — the first link of the
GPU stack takes minutes). Do **not** pass `-j`. (Same failure family as the
tilemaker-v3 note in the repo's top-level `CLAUDE.md`.)

## Experimental dependency

`dioxus-native` is experimental; versions are pinned exactly in `Cargo.toml`
(`dioxus`/`dioxus-native` `=0.7.9`, `vello` `=0.6.0`) with `Cargo.lock`
committed. Treat a breaking upgrade as a separate chore. Depending on `app_lib`
also pulls the Tauri/wry/wgpu-0.19 graph into the build (compile weight only — no
WebView is instantiated at runtime, verified: zero `msedgewebview2` processes).
