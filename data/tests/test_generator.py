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
