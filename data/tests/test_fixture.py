import sqlite3
from pathlib import Path


def test_fixture_row_count():
    db_path = Path(__file__).resolve().parents[1] / "ride_small.db"
    with sqlite3.connect(db_path) as conn:
        count = conn.execute("SELECT COUNT(*) FROM samples").fetchone()[0]
    conn.close()
    assert count == 100


def test_fixture_has_enum_event():
    db_path = Path(__file__).resolve().parents[1] / "ride_small.db"
    with sqlite3.connect(db_path) as conn:
        total = conn.execute("SELECT SUM(inu_mode2) FROM samples").fetchone()[0]
    conn.close()
    assert total >= 1


def test_fixture_columns():
    db_path = Path(__file__).resolve().parents[1] / "ride_small.db"
    with sqlite3.connect(db_path) as conn:
        cols = conn.execute("PRAGMA table_info(samples)").fetchall()
    conn.close()
    col_names = [c[1] for c in cols]
    assert len(col_names) == 31
    assert col_names[0] == "ts"
