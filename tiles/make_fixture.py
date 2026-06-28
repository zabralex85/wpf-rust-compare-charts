#!/usr/bin/env python3
"""Build a tiny committed MBTiles fixture for Rust tile-server tests.
Dummy gzipped MVT payloads — the server serves blobs verbatim, it does not parse MVT."""
import argparse, gzip, json, sqlite3

def build(out: str) -> None:
    con = sqlite3.connect(out)
    con.executescript(
        "DROP TABLE IF EXISTS tiles; DROP TABLE IF EXISTS metadata;"
        "CREATE TABLE metadata (name TEXT, value TEXT);"
        "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);"
        "CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row);"
    )
    # A few tiles across 2 zooms. tile_row is TMS (bottom-left origin).
    payloads = [(0, 0, 0), (1, 0, 0), (1, 1, 1)]
    for z, x, y in payloads:
        blob = gzip.compress(f"fixture-tile-{z}-{x}-{y}".encode())
        con.execute("INSERT INTO tiles VALUES (?,?,?,?)", (z, x, y, blob))
    meta = {
        "name": "fixture", "format": "pbf", "minzoom": "0", "maxzoom": "1",
        "bounds": "34.2,29.4,35.9,33.4", "center": "34.8,31.5,1",
        "json": json.dumps({"vector_layers": [{"id": "water"}, {"id": "transportation"}, {"id": "place"}]}),
    }
    for k, v in meta.items():
        con.execute("INSERT INTO metadata VALUES (?,?)", (k, v))
    con.commit()
    con.close()

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="fixture.mbtiles")
    build(ap.parse_args().out)
