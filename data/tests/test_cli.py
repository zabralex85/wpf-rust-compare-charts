import sqlite3
from pathlib import Path
from simulate import main


def test_cli_writes_db(tmp_path):
    out = tmp_path / "r.db"
    rc = main(["--out", str(out), "--duration", "5", "--rate", "10"])
    assert rc == 0
    assert out.exists()
    with sqlite3.connect(out) as conn:
        assert conn.execute("SELECT COUNT(*) FROM samples").fetchone()[0] == 50
    conn.close()
