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
