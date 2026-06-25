from telemetry.channels import CHANNELS, ENUM_VALUES, channel_by_column, Channel


def test_thirty_channels_with_unique_ids_and_columns():
    assert len(CHANNELS) == 30
    assert [c.id for c in CHANNELS] == list(range(1, 31))
    assert len({c.column for c in CHANNELS}) == 30
    assert len({c.name for c in CHANNELS}) == 30


def test_widgets_and_types_are_valid():
    valid_types = {"real", "enum", "hex", "text", "time"}
    valid_widgets = {"strip", "gauge", "table", "map_lat", "map_lon"}
    for c in CHANNELS:
        assert c.type in valid_types
        assert c.widget in valid_widgets
        assert c.min <= c.max


def test_has_gps_and_enum_channels():
    cols = {c.column for c in CHANNELS}
    assert {"lat", "lon"} <= cols
    assert channel_by_column("lat").widget == "map_lat"
    assert channel_by_column("lon").widget == "map_lon"
    assert "inu_mode2" in ENUM_VALUES
    codes = [code for code, _label, _sev in ENUM_VALUES["inu_mode2"]]
    assert codes == [0, 1]


def test_channel_by_column_raises_on_unknown():
    import pytest
    with pytest.raises(KeyError):
        channel_by_column("nope")
