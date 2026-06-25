import random
from telemetry import signals
from telemetry.channels import channel_by_column


def test_real_value_stays_in_range():
    ch = channel_by_column("roll")
    rng = random.Random(1)
    for i in range(1000):
        v = signals.real_value(ch, i / 10.0, rng)
        assert ch.min <= v <= ch.max


def test_real_value_deterministic_for_seed():
    ch = channel_by_column("pitch")
    a = [signals.real_value(ch, i / 10.0, random.Random(7)) for i in range(5)]
    b = [signals.real_value(ch, i / 10.0, random.Random(7)) for i in range(5)]
    assert a == b


def test_gps_track_length_and_bounds():
    rng = random.Random(2)
    lats, lons = signals.gps_track(500, rng, 32.08, 34.78)
    assert len(lats) == len(lons) == 500
    assert all(31.0 <= x <= 33.0 for x in lats)
    assert all(34.0 <= x <= 35.0 for x in lons)
    assert lats[0] == 32.08 and lons[0] == 34.78


def test_enum_series_mostly_zero_with_some_events():
    rng = random.Random(3)
    series = signals.enum_series(5000, rng, p_event=0.01)
    assert set(series) <= {0, 1}
    ones = sum(series)
    assert 0 < ones < 5000  # some events, not all
