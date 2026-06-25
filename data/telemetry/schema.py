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
