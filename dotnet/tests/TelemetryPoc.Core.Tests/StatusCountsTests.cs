using TelemetryPoc.App.Viz;

namespace TelemetryPoc.Core.Tests;

public class StatusCountsTests
{
	private static ChannelMeta Enum(long id, string col) => new(id, col, col, "", "enum", 0, 1, "table", id, "I_01");
	private static ChannelMeta Real(long id, string col) => new(id, col, col, "deg", "real", -1, 1, "table", id, "I_01");

	[Fact]
	public void Alarms_count_enum_critical_channels()
	{
		var idx = ValueFormat.BuildEnumIndex(new[]
		{
			new EnumValue(1, 0, "Normal", "ok"),
			new EnumValue(1, 1, "Critical", "critical"),
		});
		var channels = new[] { Enum(1, "mode"), Real(2, "roll") };
		double? Latest(long id) => id == 1 ? 1.0 : 0.5; // mode=Critical, roll=ok
		var (alarms, cautions) = StatusCounts.Compute(channels, Latest, idx);
		Assert.Equal(1, alarms);
		Assert.Equal(0, cautions);
	}

	[Fact]
	public void No_alarms_when_enum_is_ok()
	{
		var idx = ValueFormat.BuildEnumIndex(new[] { new EnumValue(1, 0, "Normal", "ok") });
		var channels = new[] { Enum(1, "mode") };
		var (alarms, _) = StatusCounts.Compute(channels, _ => 0.0, idx);
		Assert.Equal(0, alarms);
	}
}