# Telemetry Simulator (data/) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Python tool that generates a SQLite `ride.db` holding a 12h, ~30-channel @ 10 Hz telemetry ride, plus a committed 10s `ride_small.db` test fixture.

**Architecture:** A small package under `data/telemetry/` splits responsibilities: channel definitions, schema DDL, signal math, and a generator that orchestrates them. A thin `simulate.py` CLI drives generation. Stateless signal functions take an injected RNG for determinism; stateful series (GPS walk, enum events) are precomputed as arrays.

**Tech Stack:** Python 3.11+ standard library (`sqlite3`, `math`, `random`, `argparse`, `dataclasses`). `pytest` for tests only.

## Global Constraints

- Generation code uses the Python **standard library only** — no third-party runtime deps. `pytest` is a dev/test dep.
- Python **3.11+** (uses `list[T]` builtin generics, `match` not required).
- `ts` is **integer milliseconds** from ride start, monotonic, step = `1000 / rate_hz`.
- Default ride: `rate_hz = 10`, `duration_s = 43200` (12h) → 432,000 rows.
- `samples` is a **wide** table: `ts INTEGER PRIMARY KEY` + one column per channel, in `CHANNELS` declaration order.
- Channel SQL column type: `enum`/`hex` → `INTEGER`, `text`/`time` → `TEXT`, `real` → `REAL`.
- Generation is **deterministic** for a fixed seed (default `42`).
- `ride.db` is gitignored; `ride_small.db` (10s) is committed.

---

### Task 1: Package scaffold + channel definitions

**Files:**
- Create: `data/telemetry/__init__.py`
- Create: `data/telemetry/channels.py`
- Create: `data/tests/__init__.py`
- Create: `data/tests/test_channels.py`
- Create: `data/requirements-dev.txt`
- Create: `data/.gitignore`

**Interfaces:**
- Produces:
  - `Channel` frozen dataclass with fields `id:int, name:str, column:str, unit:str, type:str, min:float, max:float, widget:str, display_order:int, addr:str`
  - `CHANNELS: list[Channel]` — exactly 30 channels, `id` 1..30, unique `column` names, in display order
  - `ENUM_VALUES: dict[str, list[tuple[int,str,str]]]` — keyed by column → list of `(code, label, severity)`
  - `channel_by_column(col: str) -> Channel`

- [ ] **Step 1: Write the failing test**

```python
# data/tests/test_channels.py
from telemetry.channels import CHANNELS, ENUM_VALUES, channel_by_column, Channel


def test_thirty_channels_with_unique_ids_and_columns():
    assert len(CHANNELS) == 30
    assert [c.id for c in CHANNELS] == list(range(1, 31))
    assert len({c.column for c in CHANNELS}) == 30
    assert len({c.name for c in CHANNELS}) == 30


def test_widgets_and_types_are_valid():
    valid_types = {"real", "enum", "hex", "text", "time"}
    valid_widgets = {"strip", "gauge", "table", "map_lat", "map_lon"}
    for c in CHANNELS:
        assert c.type in valid_types
        assert c.widget in valid_widgets
        assert c.min <= c.max


def test_has_gps_and_enum_channels():
    cols = {c.column for c in CHANNELS}
    assert {"lat", "lon"} <= cols
    assert channel_by_column("lat").widget == "map_lat"
    assert channel_by_column("lon").widget == "map_lon"
    assert "inu_mode2" in ENUM_VALUES
    codes = [code for code, _label, _sev in ENUM_VALUES["inu_mode2"]]
    assert codes == [0, 1]


def test_channel_by_column_raises_on_unknown():
    import pytest
    with pytest.raises(KeyError):
        channel_by_column("nope")
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `data/`): `python -m pytest tests/test_channels.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'telemetry'`

- [ ] **Step 3: Write minimal implementation**

```python
# data/telemetry/__init__.py
```

```python
# data/telemetry/channels.py
from dataclasses import dataclass


@dataclass(frozen=True)
class Channel:
    id: int
    name: str
    column: str
    unit: str
    type: str          # real | enum | hex | text | time
    min: float
    max: float
    widget: str        # strip | gauge | table | map_lat | map_lon
    display_order: int
    addr: str


def _c(id, name, column, unit, type, lo, hi, widget, addr):
    return Channel(id, name, column, unit, type, lo, hi, widget, id, addr)


CHANNELS: list[Channel] = [
    _c(1,  "I0110Roll",        "roll",        "deg",  "real", -180, 180, "strip", "I_01"),
    _c(2,  "I0111Pitch",       "pitch",       "deg",  "real",  -90,  90, "strip", "I_01"),
    _c(3,  "I0112HeadingT",    "heading_t",   "deg",  "real",    0, 360, "table", "I_01"),
    _c(4,  "I0113HeadingM",    "heading_m",   "deg",  "real",    0, 360, "table", "I_01"),
    _c(5,  "I0114PlatAccX",    "acc_x",       "g",    "real",   -4,   4, "strip", "I_01"),
    _c(6,  "I0115PlatAccY",    "acc_y",       "g",    "real",   -4,   4, "strip", "I_01"),
    _c(7,  "I0116PlatAccZ",    "acc_z",       "g",    "real",   -8,   8, "strip", "I_01"),
    _c(8,  "I0103PlatVelX",    "vel_x",       "m/s",  "real", -400, 400, "table", "I_01"),
    _c(9,  "I0105PlatVelY",    "vel_y",       "m/s",  "real", -400, 400, "table", "I_01"),
    _c(10, "I0107PlatVelZ",    "vel_z",       "m/s",  "real", -100, 100, "table", "I_01"),
    _c(11, "I0109PlatAzim",    "plat_azim",   "deg",  "real", -180, 180, "table", "I_01"),
    _c(12, "I0125AltI",        "alt_i",       "m",    "real",    0, 12000, "table", "I_01"),
    _c(13, "I0126GCSErr",      "gcs_err",     "-",    "real",   -5,   5, "table", "I_01"),
    _c(14, "I0101INUMode1",    "inu_mode1",   "-",    "real",    0, 255, "table", "I_01"),
    _c(15, "I0129INUMode2",    "inu_mode2",   "-",    "enum",    0,   1, "table", "I_01"),
    _c(16, "Vclimb",           "vclimb",      "m/s",  "real", -300, 300, "table", "I_01"),
    _c(17, "SkyPitch",         "sky_pitch",   "g",    "real",   -4,   4, "gauge", "I_01"),
    _c(18, "SkyRoll",          "sky_roll",    "deg",  "real", -180, 180, "gauge", "I_01"),
    _c(19, "SkyAzim",          "sky_azim",    "deg",  "real", -180, 180, "table", "I_01"),
    _c(20, "SkyHeadingT",      "sky_heading", "deg",  "real",    0, 360, "table", "I_01"),
    _c(21, "I0130RollR",       "roll_r",      "deg/s","real",  -50,  50, "table", "I_01"),
    _c(22, "I0131PitchR",      "pitch_r",     "deg/s","real",  -50,  50, "table", "I_01"),
    _c(23, "I0132YawR",        "yaw_r",       "deg/s","real",  -50,  50, "table", "I_01"),
    _c(24, "I0102VTimeTag",    "vtime_tag",   "s",    "real",    0, 600000, "table", "I_01"),
    _c(25, "I0612PrsntTruHead","prsnt_head",  "deg",  "real", -180, 180, "table", "I_06"),
    _c(26, "GCSRange",         "gcs_range",   "m",    "real",    0, 50000, "table", "I_09"),
    _c(27, "PlatTemp",         "temp",        "C",    "real",  -20,  80, "table", "I_01"),
    _c(28, "BusVoltage",       "voltage",     "V",    "real",   22,  30, "table", "I_01"),
    _c(29, "I0915AccLat",      "lat",         "deg",  "real",   31,  33, "map_lat", "I_09"),
    _c(30, "I0915AccLon",      "lon",         "deg",  "real",   34,  35, "map_lon", "I_09"),
]

ENUM_VALUES: dict[str, list[tuple[int, str, str]]] = {
    "inu_mode2": [(0, "Normal", "ok"), (1, "Critical", "critical")],
}

_BY_COLUMN = {c.column: c for c in CHANNELS}


def channel_by_column(col: str) -> Channel:
    return _BY_COLUMN[col]
```

```
# data/requirements-dev.txt
pytest>=8.0
```

```
# data/.gitignore
ride.db
ride.db-wal
ride.db-shm
__pycache__/
.pytest_cache/
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `data/`): `python -m pytest tests/test_channels.py -v`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add data/telemetry/__init__.py data/telemetry/channels.py data/tests/__init__.py data/tests/test_channels.py data/requirements-dev.txt data/.gitignore
git commit -m "feat(data): channel definitions for telemetry simulator"
```

---

### Task 2: Schema DDL

**Files:**
- Create: `data/telemetry/schema.py`
- Create: `data/tests/test_schema.py`

**Interfaces:**
- Consumes: `CHANNELS`, `Channel` from `telemetry.channels`
- Produces:
  - `column_sql_type(ch: Channel) -> str` → `"INTEGER" | "TEXT" | "REAL"`
  - `create_schema(conn: sqlite3.Connection) -> None` — creates tables `channels`, `enum_values`, `ride_meta`, `samples`

- [ ] **Step 1: Write the failing test**

```python
# data/tests/test_schema.py
import sqlite3
from telemetry.schema import create_schema, column_sql_type
from telemetry.channels import CHANNELS, channel_by_column


def _tables(conn):
    rows = conn.execute(
        "SELECT name FROM sqlite_master WHERE type='table'"
    ).fetchall()
    return {r[0] for r in rows}


def test_creates_all_tables():
    conn = sqlite3.connect(":memory:")
    create_schema(conn)
    assert {"channels", "enum_values", "ride_meta", "samples"} <= _tables(conn)


def test_samples_has_ts_pk_and_one_column_per_channel():
    conn = sqlite3.connect(":memory:")
    create_schema(conn)
    cols = conn.execute("PRAGMA table_info(samples)").fetchall()
    names = [c[1] for c in cols]
    assert names[0] == "ts"
    assert names[1:] == [c.column for c in CHANNELS]
    ts_col = next(c for c in cols if c[1] == "ts")
    assert ts_col[5] == 1  # pk flag


def test_column_sql_type_mapping():
    assert column_sql_type(channel_by_column("roll")) == "REAL"
    assert column_sql_type(channel_by_column("inu_mode2")) == "INTEGER"
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `data/`): `python -m pytest tests/test_schema.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'telemetry.schema'`

- [ ] **Step 3: Write minimal implementation**

```python
# data/telemetry/schema.py
import sqlite3
from .channels import CHANNELS, Channel


def column_sql_type(ch: Channel) -> str:
    if ch.type in ("enum", "hex"):
        return "INTEGER"
    if ch.type in ("text", "time"):
        return "TEXT"
    return "REAL"


def create_schema(conn: sqlite3.Connection) -> None:
    cur = conn.cursor()
    cur.execute(
        """
        CREATE TABLE channels (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            column_name TEXT NOT NULL,
            unit TEXT,
            type TEXT NOT NULL,
            min REAL,
            max REAL,
            widget TEXT NOT NULL,
            display_order INTEGER NOT NULL,
            addr TEXT
        )
        """
    )
    cur.execute(
        """
        CREATE TABLE enum_values (
            channel_id INTEGER NOT NULL,
            code INTEGER NOT NULL,
            label TEXT NOT NULL,
            severity TEXT NOT NULL,
            PRIMARY KEY (channel_id, code)
        )
        """
    )
    cur.execute(
        """
        CREATE TABLE ride_meta (
            start_time INTEGER NOT NULL,
            duration_s INTEGER NOT NULL,
            rate_hz INTEGER NOT NULL,
            channel_count INTEGER NOT NULL
        )
        """
    )
    cols = ", ".join(f"{c.column} {column_sql_type(c)}" for c in CHANNELS)
    cur.execute(f"CREATE TABLE samples (ts INTEGER PRIMARY KEY, {cols})")
    conn.commit()
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `data/`): `python -m pytest tests/test_schema.py -v`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add data/telemetry/schema.py data/tests/test_schema.py
git commit -m "feat(data): sqlite schema DDL for channels and wide samples table"
```

---

### Task 3: Signal generators

**Files:**
- Create: `data/telemetry/signals.py`
- Create: `data/tests/test_signals.py`

**Interfaces:**
- Consumes: `Channel` from `telemetry.channels`
- Produces:
  - `real_value(ch: Channel, t_s: float, rng: random.Random) -> float` — within `[ch.min, ch.max]`
  - `gps_track(n: int, rng: random.Random, start_lat: float, start_lon: float) -> tuple[list[float], list[float]]`
  - `enum_series(n: int, rng: random.Random, p_event: float) -> list[int]` — values in `{0, 1}`

- [ ] **Step 1: Write the failing test**

```python
# data/tests/test_signals.py
import random
from telemetry import signals
from telemetry.channels import channel_by_column


def test_real_value_stays_in_range():
    ch = channel_by_column("roll")
    rng = random.Random(1)
    for i in range(1000):
        v = signals.real_value(ch, i / 10.0, rng)
        assert ch.min <= v <= ch.max


def test_real_value_deterministic_for_seed():
    ch = channel_by_column("pitch")
    a = [signals.real_value(ch, i / 10.0, random.Random(7)) for i in range(5)]
    b = [signals.real_value(ch, i / 10.0, random.Random(7)) for i in range(5)]
    assert a == b


def test_gps_track_length_and_bounds():
    rng = random.Random(2)
    lats, lons = signals.gps_track(500, rng, 32.08, 34.78)
    assert len(lats) == len(lons) == 500
    assert all(31.0 <= x <= 33.0 for x in lats)
    assert all(34.0 <= x <= 35.0 for x in lons)
    assert lats[0] == 32.08 and lons[0] == 34.78


def test_enum_series_mostly_zero_with_some_events():
    rng = random.Random(3)
    series = signals.enum_series(5000, rng, p_event=0.01)
    assert set(series) <= {0, 1}
    ones = sum(series)
    assert 0 < ones < 5000  # some events, not all
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `data/`): `python -m pytest tests/test_signals.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'telemetry.signals'`

- [ ] **Step 3: Write minimal implementation**

```python
# data/telemetry/signals.py
import math
import random
from .channels import Channel


def _clamp(v: float, lo: float, hi: float) -> float:
    return lo if v < lo else hi if v > hi else v


def real_value(ch: Channel, t_s: float, rng: random.Random) -> float:
    mid = (ch.min + ch.max) / 2.0
    amp = (ch.max - ch.min) / 2.0
    period = 5.0 + (ch.id % 7) * 3.0          # 5..23 s, decorrelated per channel
    base = mid + amp * 0.6 * math.sin(2 * math.pi * t_s / period)
    noise = rng.gauss(0.0, amp * 0.05)
    return _clamp(base + noise, ch.min, ch.max)


def gps_track(
    n: int, rng: random.Random, start_lat: float, start_lon: float
) -> tuple[list[float], list[float]]:
    lats: list[float] = []
    lons: list[float] = []
    lat, lon = start_lat, start_lon
    for _ in range(n):
        lats.append(lat)
        lons.append(lon)
        lat = _clamp(lat + rng.gauss(0.0, 0.0002), 31.0, 33.0)
        lon = _clamp(lon + rng.gauss(0.0, 0.0002), 34.0, 35.0)
    return lats, lons


def enum_series(n: int, rng: random.Random, p_event: float) -> list[int]:
    out: list[int] = []
    state = 0
    remaining = 0
    for _ in range(n):
        if remaining > 0:
            remaining -= 1
            state = 1
        else:
            state = 0
            if rng.random() < p_event:
                remaining = rng.randint(10, 50)  # event lasts 1-5s @10Hz
        out.append(state)
    return out
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `data/`): `python -m pytest tests/test_signals.py -v`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add data/telemetry/signals.py data/tests/test_signals.py
git commit -m "feat(data): deterministic signal, GPS track, and enum-event generators"
```

---

### Task 4: Generator orchestration

**Files:**
- Create: `data/telemetry/generator.py`
- Create: `data/tests/test_generator.py`

**Interfaces:**
- Consumes: `CHANNELS`, `ENUM_VALUES` from `telemetry.channels`; `create_schema` from `telemetry.schema`; `real_value`, `gps_track`, `enum_series` from `telemetry.signals`
- Produces:
  - `generate(conn: sqlite3.Connection, duration_s: int, rate_hz: int = 10, seed: int = 42, start_time: int = 0) -> int` — populates all tables, returns the number of sample rows written

- [ ] **Step 1: Write the failing test**

```python
# data/tests/test_generator.py
import sqlite3
from telemetry.generator import generate
from telemetry.channels import CHANNELS


def _gen(duration_s=10, rate_hz=10):
    conn = sqlite3.connect(":memory:")
    n = generate(conn, duration_s=duration_s, rate_hz=rate_hz, seed=42)
    return conn, n


def test_row_count_matches_duration_and_rate():
    conn, n = _gen(duration_s=10, rate_hz=10)
    assert n == 100
    assert conn.execute("SELECT COUNT(*) FROM samples").fetchone()[0] == 100


def test_timestamps_monotonic_and_stepped():
    conn, _ = _gen()
    ts = [r[0] for r in conn.execute("SELECT ts FROM samples ORDER BY ts")]
    assert ts[0] == 0
    assert all(b - a == 100 for a, b in zip(ts, ts[1:]))


def test_channels_and_enums_and_meta_populated():
    conn, _ = _gen()
    assert conn.execute("SELECT COUNT(*) FROM channels").fetchone()[0] == len(CHANNELS)
    assert conn.execute(
        "SELECT COUNT(*) FROM enum_values WHERE channel_id="
        "(SELECT id FROM channels WHERE column_name='inu_mode2')"
    ).fetchone()[0] == 2
    meta = conn.execute(
        "SELECT duration_s, rate_hz, channel_count FROM ride_meta"
    ).fetchone()
    assert meta == (10, 10, len(CHANNELS))


def test_deterministic_for_seed():
    c1, _ = _gen()
    c2, _ = _gen()
    r1 = c1.execute("SELECT roll FROM samples ORDER BY ts").fetchall()
    r2 = c2.execute("SELECT roll FROM samples ORDER BY ts").fetchall()
    assert r1 == r2
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `data/`): `python -m pytest tests/test_generator.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'telemetry.generator'`

- [ ] **Step 3: Write minimal implementation**

```python
# data/telemetry/generator.py
import random
import sqlite3
from .channels import CHANNELS, ENUM_VALUES
from .schema import create_schema
from . import signals


def _populate_channels(conn: sqlite3.Connection) -> None:
    conn.executemany(
        "INSERT INTO channels "
        "(id, name, column_name, unit, type, min, max, widget, display_order, addr) "
        "VALUES (?,?,?,?,?,?,?,?,?,?)",
        [
            (c.id, c.name, c.column, c.unit, c.type, c.min, c.max,
             c.widget, c.display_order, c.addr)
            for c in CHANNELS
        ],
    )
    col_to_id = {c.column: c.id for c in CHANNELS}
    enum_rows = [
        (col_to_id[col], code, label, sev)
        for col, vals in ENUM_VALUES.items()
        for (code, label, sev) in vals
    ]
    conn.executemany(
        "INSERT INTO enum_values (channel_id, code, label, severity) VALUES (?,?,?,?)",
        enum_rows,
    )


def generate(
    conn: sqlite3.Connection,
    duration_s: int,
    rate_hz: int = 10,
    seed: int = 42,
    start_time: int = 0,
) -> int:
    rng = random.Random(seed)
    create_schema(conn)
    _populate_channels(conn)

    n = int(duration_s * rate_hz)
    step_ms = int(1000 / rate_hz)

    lats, lons = signals.gps_track(n, rng, 32.08, 34.78)
    modes = signals.enum_series(n, rng, p_event=0.001)

    placeholders = ",".join(["?"] * (1 + len(CHANNELS)))
    insert_sql = f"INSERT INTO samples VALUES ({placeholders})"

    rows = []
    for i in range(n):
        t_s = i / rate_hz
        row = [i * step_ms]
        for ch in CHANNELS:
            if ch.column == "lat":
                row.append(lats[i])
            elif ch.column == "lon":
                row.append(lons[i])
            elif ch.column == "inu_mode2":
                row.append(modes[i])
            elif ch.type == "real":
                row.append(signals.real_value(ch, t_s, rng))
            else:
                row.append(0)
        rows.append(row)

    conn.execute("PRAGMA journal_mode=WAL")
    conn.executemany(insert_sql, rows)
    conn.execute(
        "INSERT INTO ride_meta VALUES (?,?,?,?)",
        (start_time, duration_s, rate_hz, len(CHANNELS)),
    )
    conn.commit()
    return n
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `data/`): `python -m pytest tests/test_generator.py -v`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add data/telemetry/generator.py data/tests/test_generator.py
git commit -m "feat(data): generator orchestrates schema, signals, and bulk insert"
```

---

### Task 5: CLI + fixture + docs

**Files:**
- Create: `data/simulate.py`
- Create: `data/README.md`
- Create: `data/ride_small.db` (generated, committed)
- Create: `data/tests/test_cli.py`

**Interfaces:**
- Consumes: `generate` from `telemetry.generator`
- Produces: `main(argv: list[str] | None = None) -> int` — CLI entry; writes a SQLite file

- [ ] **Step 1: Write the failing test**

```python
# data/tests/test_cli.py
import sqlite3
from pathlib import Path
from simulate import main


def test_cli_writes_db(tmp_path):
    out = tmp_path / "r.db"
    rc = main(["--out", str(out), "--duration", "5", "--rate", "10"])
    assert rc == 0
    assert out.exists()
    conn = sqlite3.connect(out)
    assert conn.execute("SELECT COUNT(*) FROM samples").fetchone()[0] == 50
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `data/`): `python -m pytest tests/test_cli.py -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'simulate'`

- [ ] **Step 3: Write minimal implementation**

```python
# data/simulate.py
import argparse
import os
import sqlite3
import sys
from telemetry.generator import generate


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Generate a telemetry ride SQLite DB.")
    p.add_argument("--out", default="ride.db", help="output sqlite path")
    p.add_argument("--duration", type=int, default=43200, help="ride seconds (default 12h)")
    p.add_argument("--rate", type=int, default=10, help="sample rate Hz")
    p.add_argument("--seed", type=int, default=42)
    args = p.parse_args(argv)

    if os.path.exists(args.out):
        os.remove(args.out)
    conn = sqlite3.connect(args.out)
    try:
        n = generate(conn, duration_s=args.duration, rate_hz=args.rate, seed=args.seed)
    finally:
        conn.close()
    print(f"wrote {n} rows to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `data/`): `python -m pytest tests/test_cli.py -v`
Expected: PASS (1 test)

- [ ] **Step 5: Generate the committed fixture and full README**

Run (from `data/`):
```bash
python simulate.py --out ride_small.db --duration 10 --rate 10
```
Expected output: `wrote 100 rows to ride_small.db`

```markdown
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
```

- [ ] **Step 6: Commit**

```bash
git add data/simulate.py data/README.md data/ride_small.db data/tests/test_cli.py
git commit -m "feat(data): simulate.py CLI, committed 10s fixture, and README"
```

---

## Self-Review

**Spec coverage:**
- §3 simulator + stdlib-only → Tasks 1-5 ✓
- §4 schema (channels/enum_values/samples wide/ride_meta) → Task 2 ✓; channel set ~30 → Task 1 ✓; signal generation + GPS + enum events → Task 3 ✓; bulk insert + WAL → Task 4 ✓
- §10 defaults (ts ms, rate 10, seed) → Tasks 4-5 ✓
- §9 testing (schema, monotonic ts, counts, ranges; committed `ride_small.db`) → Tasks 2-5 ✓
- `ride.db` gitignored → Task 1 ✓

**Placeholder scan:** No TBD/TODO; all steps carry real code and commands. ✓

**Type consistency:** `Channel.column` used consistently; `create_schema` stores it as `column_name` (SQL reserved-word avoidance) and queries in Tasks 4-5 use `column_name`. `generate(...)` signature consistent between Task 4 definition and Task 5 call. `real_value/gps_track/enum_series` signatures match between Task 3 and Task 4 usage. ✓

> **Note for Plans 2 & 3:** apps read channel metadata from `channels.column_name` (not `column`), and `samples` columns are in `CHANNELS` declaration order with `ts` first.
