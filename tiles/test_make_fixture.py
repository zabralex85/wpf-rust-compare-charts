import gzip, sqlite3, os, subprocess, sys

def test_fixture_built(tmp_path):
    out = tmp_path / "fixture.mbtiles"
    subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), "make_fixture.py"), "--out", str(out)], check=True)
    con = sqlite3.connect(out)
    # schema
    names = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    assert {"tiles", "metadata"} <= names
    # at least one tile, gzipped
    z, x, y, blob = con.execute("SELECT zoom_level,tile_column,tile_row,tile_data FROM tiles LIMIT 1").fetchone()
    assert gzip.decompress(blob)  # decompresses
    # metadata format=pbf + zoom bounds present
    meta = dict(con.execute("SELECT name,value FROM metadata"))
    assert meta["format"] == "pbf"
    assert "minzoom" in meta and "maxzoom" in meta and "bounds" in meta
    con.close()
