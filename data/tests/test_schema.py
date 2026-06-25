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
