namespace TelemetryPoc.Core.Tests;

public class ValueFormatTests
{
    private static readonly IReadOnlyDictionary<(long, long), EnumValue> Idx =
        ValueFormat.BuildEnumIndex(new[]
        {
            new EnumValue(15, 0, "Normal", "ok"),
            new EnumValue(15, 1, "Critical", "critical"),
        });

    private static ChannelMeta Ch(string type, long id = 1) =>
        new(id, "x", "x", "", type, 0, 1, "table", 1, "");

    [Fact]
    public void DecodeEnum_rounds_and_resolves()
    {
        Assert.Equal("Critical", ValueFormat.DecodeEnum(15, 0.7, Idx)?.Label);
        Assert.Null(ValueFormat.DecodeEnum(15, 9, Idx));
    }

    [Fact]
    public void FormatValue_enum_to_label()
        => Assert.Equal("Critical", ValueFormat.FormatValue(Ch("enum", 15), 1, Idx));

    [Fact]
    public void FormatValue_real_to_three_decimals_invariant()
        => Assert.Equal("1.235", ValueFormat.FormatValue(Ch("real"), 1.23456, Idx));

    [Fact]
    public void SeverityColor_maps_known_and_default()
    {
        Assert.Equal("#d22", ValueFormat.SeverityColor("critical"));
        Assert.Equal("#2a2", ValueFormat.SeverityColor("ok"));
        Assert.Equal("#888", ValueFormat.SeverityColor(null));
    }
}
