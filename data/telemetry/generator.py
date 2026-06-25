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
    conn.execute("PRAGMA journal_mode=WAL")
    rng = random.Random(seed)
    create_schema(conn)
    _populate_channels(conn)

    n = int(duration_s * rate_hz)
    step_ms = int(1000 / rate_hz)

    lats, lons = signals.gps_track(n, rng, 32.08, 34.78)
    modes = signals.enum_series(n, rng, p_event=0.001)

    col_list = ", ".join(["ts"] + [c.column for c in CHANNELS])
    placeholders = ",".join(["?"] * (1 + len(CHANNELS)))
    insert_sql = f"INSERT INTO samples ({col_list}) VALUES ({placeholders})"

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

    conn.executemany(insert_sql, rows)
    conn.execute(
        "INSERT INTO ride_meta VALUES (?,?,?,?)",
        (start_time, duration_s, rate_hz, len(CHANNELS)),
    )
    conn.commit()
    return n
