from dataclasses import dataclass


@dataclass(frozen=True)
class Channel:
    id: int
    name: str
    column: str
    unit: str
    type: str          # real | enum | hex | text | time
    min: float
    max: float
    widget: str        # strip | gauge | table | map_lat | map_lon
    display_order: int
    addr: str


def _c(id, name, column, unit, type, lo, hi, widget, addr):
    return Channel(id, name, column, unit, type, lo, hi, widget, id, addr)


CHANNELS: list[Channel] = [
    _c(1,  "I0110Roll",        "roll",        "deg",  "real", -180, 180, "strip", "I_01"),
    _c(2,  "I0111Pitch",       "pitch",       "deg",  "real",  -90,  90, "strip", "I_01"),
    _c(3,  "I0112HeadingT",    "heading_t",   "deg",  "real",    0, 360, "table", "I_01"),
    _c(4,  "I0113HeadingM",    "heading_m",   "deg",  "real",    0, 360, "table", "I_01"),
    _c(5,  "I0114PlatAccX",    "acc_x",       "g",    "real",   -4,   4, "strip", "I_01"),
    _c(6,  "I0115PlatAccY",    "acc_y",       "g",    "real",   -4,   4, "strip", "I_01"),
    _c(7,  "I0116PlatAccZ",    "acc_z",       "g",    "real",   -8,   8, "strip", "I_01"),
    _c(8,  "I0103PlatVelX",    "vel_x",       "m/s",  "real", -400, 400, "table", "I_01"),
    _c(9,  "I0105PlatVelY",    "vel_y",       "m/s",  "real", -400, 400, "table", "I_01"),
    _c(10, "I0107PlatVelZ",    "vel_z",       "m/s",  "real", -100, 100, "table", "I_01"),
    _c(11, "I0109PlatAzim",    "plat_azim",   "deg",  "real", -180, 180, "table", "I_01"),
    _c(12, "I0125AltI",        "alt_i",       "m",    "real",    0, 12000, "table", "I_01"),
    _c(13, "I0126GCSErr",      "gcs_err",     "-",    "real",   -5,   5, "table", "I_01"),
    _c(14, "I0101INUMode1",    "inu_mode1",   "-",    "real",    0, 255, "table", "I_01"),
    _c(15, "I0129INUMode2",    "inu_mode2",   "-",    "enum",    0,   1, "table", "I_01"),
    _c(16, "Vclimb",           "vclimb",      "m/s",  "real", -300, 300, "table", "I_01"),
    _c(17, "SkyPitch",         "sky_pitch",   "g",    "real",   -4,   4, "gauge", "I_01"),
    _c(18, "SkyRoll",          "sky_roll",    "deg",  "real", -180, 180, "gauge", "I_01"),
    _c(19, "SkyAzim",          "sky_azim",    "deg",  "real", -180, 180, "table", "I_01"),
    _c(20, "SkyHeadingT",      "sky_heading", "deg",  "real",    0, 360, "table", "I_01"),
    _c(21, "I0130RollR",       "roll_r",      "deg/s","real",  -50,  50, "table", "I_01"),
    _c(22, "I0131PitchR",      "pitch_r",     "deg/s","real",  -50,  50, "table", "I_01"),
    _c(23, "I0132YawR",        "yaw_r",       "deg/s","real",  -50,  50, "table", "I_01"),
    _c(24, "I0102VTimeTag",    "vtime_tag",   "s",    "real",    0, 600000, "table", "I_01"),
    _c(25, "I0612PrsntTruHead","prsnt_head",  "deg",  "real", -180, 180, "table", "I_06"),
    _c(26, "GCSRange",         "gcs_range",   "m",    "real",    0, 50000, "table", "I_09"),
    _c(27, "PlatTemp",         "temp",        "C",    "real",  -20,  80, "table", "I_01"),
    _c(28, "BusVoltage",       "voltage",     "V",    "real",   22,  30, "table", "I_01"),
    _c(29, "I0915AccLat",      "lat",         "deg",  "real",   31,  33, "map_lat", "I_09"),
    _c(30, "I0915AccLon",      "lon",         "deg",  "real",   34,  35, "map_lon", "I_09"),
]

ENUM_VALUES: dict[str, list[tuple[int, str, str]]] = {
    "inu_mode2": [(0, "Normal", "ok"), (1, "Critical", "critical")],
}

_BY_COLUMN = {c.column: c for c in CHANNELS}


def channel_by_column(col: str) -> Channel:
    return _BY_COLUMN[col]
