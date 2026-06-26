# .NET WPF + Blazor Hybrid Dashboard

This is the .NET implementation of the telemetry charting proof-of-concept (PoC) dashboard. It's a WPF + Blazor Hybrid application that replays telemetry data from a ride database in-process and renders an interactive dashboard with strip charts, a GPS map, and parameter gauges.

## Architecture

- **WPF + Blazor Hybrid**: Windows Forms-based host with embedded Blazor components for interactive charts.
- **In-process replay**: The database is replayed frame-by-frame without WebSocket communication.
- **Charts**: ScottPlot.Blazor for time-series strip charts (bar/tick style and filled area style).
- **Map**: Leaflet + OpenStreetMap tiles for GPS track visualization and markers.
- **Layout**: Four-region dashboard (param table, main strip chart, GPS map, gauges) per `docs/reference/dashboard-target.md`.

## Prerequisites

### Database

Generate a ride database by running the Python data simulator:

```bash
python data/simulate.py
```

This creates `data/ride.db` (full 12-hour ride). For quicker testing, use the committed `data/ride_small.db`.

### .NET Runtime

- .NET 8.0 or later
- Windows 10/11 (WPF is Windows-only)
- A display is required to run the GUI (no headless mode)

### Network

The dashboard loads map tiles from OSM CDN, so internet access is required.

## Running the Dashboard

From the project root:

```bash
dotnet run --project dotnet/src/TelemetryPoc.App
```

### Environment Variables

- `RIDE_DB`: Full path to the ride database. If unset or the file is missing, the app auto-resolves by walking up from the executable directory to find `data/ride.db`, then falls back to `data/ride_small.db`.
- `RIDE_SPEED`: Replay speed multiplier (default: `1.0`). Higher values speed up playback.

### Example

Replay at 5x speed:

```bash
RIDE_SPEED=5 dotnet run --project dotnet/src/TelemetryPoc.App
```

Or with an explicit database path:

```bash
RIDE_DB=D:\data\my_ride.db dotnet run --project dotnet/src/TelemetryPoc.App
```

## Testing

Run the test suite from the `dotnet/` directory:

```bash
dotnet test
```

Expected result: **22 tests pass** (0 failures).

## Reference

For the visual dashboard design and layout specification, see `docs/reference/dashboard-target.md`.

## Build

To build the solution without running:

```bash
dotnet build
```

Expected result: **0 errors, 0 warnings**.

## Notes

- The GUI requires a display. For headless environments, use the tests to verify functionality.
- Build artifacts (bin/, obj/) are gitignored and will not be committed.
- The app may take a moment to load Leaflet tiles on first run if the cache is cold.
