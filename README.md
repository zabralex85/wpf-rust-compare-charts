# wpf-rust-compare-charts

Render the **same realtime telemetry dashboard in two stacks** and compare their performance.

An INU-style monitoring dashboard — live parameter table, scrolling strip charts, radial gauges, GPS map, and a perf HUD — is replayed from a shared SQLite "ride" and built twice:

| | Rust app | .NET app |
|---|---|---|
| Stack | Tauri 2 + React + TypeScript | WPF + Blazor Hybrid (C#) |
| Charts | uPlot | ScottPlot.Blazor |
| Map | Leaflet | Leaflet (JS interop) |
| Transport | local WebSocket | in-process |

Both read the **same `ride.db`** and show the same HUD — FPS, frame time, end-to-end latency, CPU% (per-core), RAM — so the two stacks can be compared head to head. Target layout: [`docs/reference/dashboard-target.md`](docs/reference/dashboard-target.md).

## Repository layout

```
data/    Python telemetry simulator → ride.db (shared data; gitignored)
rust/    Tauri + React + WebSocket dashboard
dotnet/  .NET solution — TelemetryPoc.Core (data layer) + TelemetryPoc.App (WPF + Blazor UI)
docs/    specs, plans, and the dashboard reference target
```

The two apps share no code — each is its stack's idiomatic implementation. They share only `ride.db` and its schema.

## Quickstart

**1. Generate the data**
```bash
python data/simulate.py                                              # full 12h ride
python data/simulate.py --out data/ride_small.db --duration 60      # or a short one
```

**2a. Run the Rust app**
```bash
cd rust && npm install
RIDE_DB=../../data/ride_small.db RIDE_SPEED=5 npm run tauri dev
```

**2b. Run the .NET app** (.NET 8, Windows)
```bash
RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App
```

Both connect to the same data and render the dashboard with the live HUD. See [`rust/README.md`](rust/README.md) and [`dotnet/README.md`](dotnet/README.md) for details and env vars (`RIDE_DB`, `RIDE_SPEED`, `RIDE_WS_PORT`). The .NET app and the Leaflet map need network access (CDN + OSM tiles).

## Tests

```bash
cd data && python -m pytest          # simulator
cd rust && npm test                  # frontend (vitest)
cd rust/src-tauri && cargo test      # Rust backend
cd dotnet && dotnet test             # .NET data layer
```

Chart/map/GUI code is build-verified; pure logic carries the unit coverage.

## Status

Both apps build, test, and run. The visual alignment of each dashboard against the reference image is the remaining step (it needs a live GUI launch).

## License

See [LICENSE](LICENSE).
