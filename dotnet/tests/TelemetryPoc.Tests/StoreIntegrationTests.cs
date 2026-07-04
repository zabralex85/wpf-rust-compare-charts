using TelemetryPoc.Domain;
using TelemetryPoc.Infrastructure;

namespace TelemetryPoc.Tests;

public class StoreIntegrationTests
{
    [Fact]
    public void Store_driven_from_fixture_db_behaves_correctly()
    {
        using var conn = Fixtures.Open();
        var channels = SqliteRideSource.LoadChannels(conn);
        var enums = SqliteRideSource.LoadEnumValues(conn);
        var samples = SqliteRideSource.LoadSamples(conn, channels);

        var store = new TelemetryStore();
        store.ApplyMeta(channels, enums);

        var roll = channels.First(c => c.ColumnName == "roll");
        var inuMode2 = channels.First(c => c.ColumnName == "inu_mode2");
        Assert.Equal("strip", roll.Widget);   // real widget string from the fixture
        Assert.Equal("table", inuMode2.Widget);

        const long emit = 1_700_000_000_000;
        foreach (var s in samples)
        {
            store.ApplyFrame(s, emit);
        }

        // latest reflects the last sample, aligned by channel index
        var rollIdx = channels.ToList().FindIndex(c => c.Id == roll.Id);
        Assert.Equal(samples[^1].Values[rollIdx], store.Latest(roll.Id));

        // strip channel has a windowed series; a table channel does not
        Assert.NotNull(store.Series(roll.Id));
        Assert.Null(store.Series(inuMode2.Id));

        // gps track accumulated, decimated to ~2 Hz (a point kept only when ts advanced ≥500 ms)
        var (lat, lon) = store.GpsTrack();
        long lastGps = 0;
        int expectedGps = 0;
        foreach (var s in samples)
        {
            if (expectedGps == 0 || s.TsMs - lastGps >= 500)
            {
                expectedGps++;
                lastGps = s.TsMs;
            }
        }

        Assert.Equal(expectedGps, lat.Count);
        Assert.True(lat.Count < samples.Count); // genuinely decimated below the raw 10 Hz frame count
        Assert.Equal(lat.Count, lon.Count);

        // enum channel formats to a decoded label via the store's enum index
        var modeVal = store.Latest(inuMode2.Id)!.Value;
        var formatted = ValueFormat.FormatValue(inuMode2, modeVal, store.EnumIndex);
        Assert.Contains(formatted, new[] { "Normal", "Critical" });

        Assert.Equal(emit, store.LastEmitUnixMs);
    }
}
