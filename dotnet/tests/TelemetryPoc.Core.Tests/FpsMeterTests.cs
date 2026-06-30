using TelemetryPoc.App.Viz;

namespace TelemetryPoc.Core.Tests;

public class FpsMeterTests
{
	[Fact]
	public void Zero_before_two_ticks()
	{
		var m = new FpsMeter();
		Assert.Equal(0, m.Fps(), 6);
		m.Tick(0);
		Assert.Equal(0, m.Fps(), 6);
	}

	[Fact]
	public void Computes_60fps_from_16_67ms_intervals()
	{
		var m = new FpsMeter();
		for (int i = 0; i <= 10; i++)
		{
			m.Tick(i * (1000.0 / 60));
		}

		Assert.Equal(60, m.Fps(), 0);
		Assert.Equal(1000.0 / 60, m.FrameTimeMs(), 1);
	}

	[Fact]
	public void Respects_window_size()
	{
		var m = new FpsMeter(3);
		for (int i = 0; i < 10; i++)
		{
			m.Tick(i * 10);
		}

		Assert.Equal(100, m.Fps(), 0); // last 3 ts → 2×10ms → 100fps
	}

	[Fact]
	public void Reset_clears_window_so_pause_gap_does_not_skew_fps()
	{
		var m = new FpsMeter();
		for (int i = 0; i <= 10; i++)
		{
			m.Tick(i * (1000.0 / 60)); // steady 60fps history
		}

		m.Reset();
		Assert.Equal(0, m.Fps(), 6); // window emptied
		// resume far later: no stale pre-pause sample to inflate the span
		m.Tick(100_000);
		m.Tick(100_000 + 1000.0 / 60);
		Assert.Equal(60, m.Fps(), 0);
	}

	[Fact]
	public void LatencyMs_is_now_minus_emit()
	{
		Assert.Equal(75, HudMetrics.LatencyMs(1000, 1075));
	}
}