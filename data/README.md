# data/ — Telemetry Simulator

Generates a SQLite `ride.db` holding a 12-hour, ~30-channel @ 10 Hz telemetry ride
that both the Rust and .NET apps replay.

## Generate the full ride (12h, ~432k rows — gitignored)

```bash
cd data
python simulate.py            # writes ride.db (defaults: 12h @ 10 Hz)
```

## Options

| Flag | Default | Meaning |
|------|---------|---------|
| `--out` | `ride.db` | output path |
| `--duration` | `43200` | ride length in seconds (12h) |
| `--rate` | `10` | sample rate (Hz) |
| `--seed` | `42` | RNG seed (deterministic) |

## Schema

- `channels` — one row per channel (name, unit, type, min/max, widget, addr). Drives the UI.
- `enum_values` — decode table for enum channels (e.g. `inu_mode2`: 0=Normal, 1=Critical).
- `samples` — wide table: `ts` (ms from start) + one column per channel, in channel order.
- `ride_meta` — start_time, duration_s, rate_hz, channel_count.

## Tests

```bash
cd data
pip install -r requirements-dev.txt
python -m pytest -v
```

`ride_small.db` (10s, committed) is the shared test fixture for the Rust and .NET apps.
