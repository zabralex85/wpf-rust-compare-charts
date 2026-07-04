# rust-egui — egui/eframe immediate-mode variant

The **fourth** dashboard variant, and the lightweight end of the comparison: an
**immediate-mode** Rust UI on [`egui`](https://github.com/emilk/egui)/`eframe`
(glow / OpenGL backend). No HTML/CSS layout engine, no Vello/WGPU compute
renderer, no `system-fonts` database — the opposite of the Dioxus-native
(Blitz/Vello) variant.

## Why it exists

To answer "why does the native app eat ~530 MB when a C++ app was ~20 MB?" —
the memory was the **framework, not the language**. This variant, built on the
leanest common Rust GUI stack, lands at:

| | RAM | CPU | Processes |
|---|---|---|---|
| **rust-egui** | **~82 MB** | ~2% | 1 |

vs Dioxus-native ~530 MB, .NET/Avalonia-Skia ~279 MB, Tauri/WebView2 ~600+ MB
(all same ride, `RIDE_SPEED=1`, Release — see the root README table). ~6.5×
lighter than Dioxus-native. Rust itself is as lean as C++; the heavy variants
pay for their rendering stack.

The residual gap to a ~20 MB C++/Dear-ImGui app is `eframe`'s bundled default
fonts + the glow GL context + `std`. A `softbuffer` + `tiny-skia` (CPU raster)
or raw Direct2D build would close most of it.

## Scope

Same reduced dashboard as rust-native: grouped parameter table (INU groups +
enum decode with severity color), one strip chart per strip channel (60 s
window, drawn with `egui::Painter` polylines), perf HUD. No map / gauges /
transport. Reuses `app_lib` (db / `replay::Pacer` / `metrics`) in-process.

The whole thing is one `src/main.rs` — immediate-mode fits a single file.

## Run

```bash
cd rust-egui
RIDE_DB=../data/ride_small.db RIDE_SPEED=1 cargo run --release
```

## Notes

- Repaints are capped to ~30 Hz (`request_repaint_after(33ms)`) — egui's default
  continuous mode free-runs at 60 Hz and pins a full core; the data is only
  10 Hz, so 30 Hz is smooth and cheap.
- `default-features = false` on `eframe` (glow only, no wgpu) keeps it light.
- Depending on `app_lib` pulls the Tauri dep graph into the build (compile
  weight only; no WebView is created at runtime).
- Prototype: pure-logic isn't split into unit-tested modules like rust-native —
  it's a single-file spike to establish the footprint floor.
