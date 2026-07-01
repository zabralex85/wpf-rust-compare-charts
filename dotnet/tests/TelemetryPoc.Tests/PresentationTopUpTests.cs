using TelemetryPoc.Domain;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

/// <summary>Fills in the missing branches flagged for MissionClock, StatusCounts, FpsMeter
/// and GaugeFormat — their existing *Tests.cs files cover the common paths but miss a few
/// edge cases (negative FormatShort input, null-latest/no-match StatusCounts rows, a
/// zero-span FpsMeter window, non-finite GaugeValue).</summary>
public class PresentationTopUpTests
{
    [Fact]
    public void MissionClock_FormatShort_clamps_negative_to_zero()
        => Assert.Equal("0:00:00", MissionClock.FormatShort(-500));

    [Fact]
    public void StatusCounts_skips_channels_with_no_latest_value()
    {
        var idx = ValueFormat.BuildEnumIndex([new EnumValue(1, 1, "Critical", "critical")]);
        var channels = new[] { new ChannelMeta(1, "mode", "mode", "", "enum", 0, 1, "table", 1, "I_01") };

        var (alarms, cautions) = StatusCounts.Compute(channels, _ => null, idx);

        Assert.Equal(0, alarms);
        Assert.Equal(0, cautions);
    }

    [Fact]
    public void StatusCounts_skips_enum_values_with_no_matching_index_entry()
    {
        var idx = ValueFormat.BuildEnumIndex([new EnumValue(1, 0, "Normal", "ok")]); // no code=9 entry
        var channels = new[] { new ChannelMeta(1, "mode", "mode", "", "enum", 0, 1, "table", 1, "I_01") };

        var (alarms, _) = StatusCounts.Compute(channels, _ => 9.0, idx);

        Assert.Equal(0, alarms);
    }

    [Fact]
    public void FpsMeter_zero_span_between_identical_timestamps_is_zero_fps()
    {
        var m = new FpsMeter();
        m.Tick(10);
        m.Tick(10);
        Assert.Equal(0, m.Fps(), 6);
    }

    [Fact]
    public void GaugeFormat_GaugeValue_non_finite_is_dash()
        => Assert.Equal("—", GaugeFormat.GaugeValue(double.PositiveInfinity));
}
