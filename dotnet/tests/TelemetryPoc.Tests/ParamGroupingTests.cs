using TelemetryPoc.Domain;
using TelemetryPoc.Presentation;

namespace TelemetryPoc.Tests;

public class ParamGroupingTests
{
	private static ChannelMeta Ch(string col, long order, string widget = "table")
		=> new(order, col.ToUpperInvariant(), col, "", "real", 0, 1, widget, order, "I_01");

	[Theory]
	[InlineData("roll", "Attitude")]
	[InlineData("vel_x", "Velocity")]
	[InlineData("acc_z", "Acceleration")]
	[InlineData("lat", "Position")]
	[InlineData("inu_mode2", "INU Mode")]
	[InlineData("something_else", "System")]
	public void GroupOf_maps_columns(string col, string group)
		=> Assert.Equal(group, ParamGrouping.GroupOf(col));

	[Fact]
	public void Group_orders_groups_and_sorts_by_display_order()
	{
		var channels = new[]
		{
			Ch("acc_z", 5), Ch("roll", 3), Ch("pitch", 2), Ch("inu_mode1", 1),
		};
		var groups = ParamGrouping.Group(channels);
		// ORDER: INU Mode before Attitude before Acceleration; empty groups dropped
		Assert.Equal(["INU Mode", "Attitude", "Acceleration"], groups.Select(g => g.Name).ToArray());
		// Attitude sorted by DisplayOrder: pitch(2) before roll(3)
		Assert.Equal(["pitch", "roll"], groups[1].Channels.Select(c => c.ColumnName).ToArray());
	}

	[Fact]
	public void Severity_hex_maps_to_inu_palette()
	{
		Assert.Equal("#FFFF4D52", Severity.Hex("critical"));
		Assert.Equal("#FF2FD17A", Severity.Hex("ok"));
		Assert.Equal("#FF566273", Severity.Hex(null));
		Assert.Equal("#FF566273", Severity.Hex("whatever"));
	}
}