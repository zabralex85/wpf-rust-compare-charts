using TelemetryPoc.App.Viz;

namespace TelemetryPoc.Core.Tests;

public class ParamRowViewTests
{
	private static ChannelMeta Real(string col)
		=> new(1, col.ToUpperInvariant(), col, "deg", "real", -10, 10, "table", 1, "I_01");
	private static ChannelMeta Enum(long id, string col)
		=> new(id, col.ToUpperInvariant(), col, "", "enum", 0, 1, "table", 1, "I_01");

	private static readonly IReadOnlyDictionary<(long, long), EnumValue> Idx = ValueFormat.BuildEnumIndex(
	[
		new EnumValue(7, 0, "Normal", "ok"),
		new EnumValue(7, 1, "Critical", "critical"),
	]);

	[Fact]
	public void Null_latest_is_dash_dim()
	{
		var d = ParamRowView.Build(Real("roll"), null, Idx);
		Assert.Equal("—", d.Text);
		Assert.Equal("#FF566273", d.ValueColor);
		Assert.Equal("#FF566273", d.DotColor);
		Assert.False(d.Critical);
	}

	[Fact]
	public void Real_row_is_ok_value_text_color()
	{
		var d = ParamRowView.Build(Real("roll"), 1.5, Idx);
		Assert.Equal("1.500", d.Text);
		Assert.Equal("#FFC3CCD8", d.ValueColor);   // non-enum ok → plain text color
		Assert.Equal("#FF2FD17A", d.DotColor);     // ok → green dot
		Assert.False(d.Critical);
	}

	[Fact]
	public void Enum_ok_is_green_value()
	{
		var d = ParamRowView.Build(Enum(7, "inu_mode1"), 0, Idx);
		Assert.Equal("Normal", d.Text);
		Assert.Equal("#FF2FD17A", d.ValueColor);   // enum ok → green value
		Assert.False(d.Critical);
	}

	[Fact]
	public void Enum_critical_is_red_and_flagged()
	{
		var d = ParamRowView.Build(Enum(7, "inu_mode2"), 1, Idx);
		Assert.Equal("Critical", d.Text);
		Assert.Equal("#FFFF4D52", d.ValueColor);
		Assert.Equal("#FFFF4D52", d.DotColor);
		Assert.True(d.Critical);
	}
}