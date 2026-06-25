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
