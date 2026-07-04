namespace TelemetryPoc.Domain;

public sealed class TelemetryStore
{
    private readonly long _windowMs;
    private readonly long _gpsIntervalMs;
    private Dictionary<long, int> _idToIndex = [];
    private double[] _latest = Array.Empty<double>();
    private readonly Dictionary<long, ChannelSeries> _series = [];
    private int _latIdx = -1, _lonIdx = -1;
    private readonly List<double> _lat = [];
    private readonly List<double> _lon = [];
    private long _lastGpsTs;

    // windowMs: strip-series scrolling window. gpsIntervalMs: minimum spacing between stored GPS
    // track points — the raw stream is ~10 Hz but the flight path only needs ~2 Hz, so decimating
    // here bounds both the track-list growth and the per-frame track rebuild (TrackOverlay SKPath).
    // 500 ms → map advances 2×/s (live enough) while still cutting the rebuild/growth ~5×.
    // Kept identical to the Rust store so the memory/CPU comparison stays fair.
    public TelemetryStore(long windowMs = 60_000, long gpsIntervalMs = 500)
    {
        _windowMs = windowMs;
        _gpsIntervalMs = gpsIntervalMs;
    }

    public IReadOnlyList<ChannelMeta> Channels { get; private set; } = Array.Empty<ChannelMeta>();
    public IReadOnlyDictionary<(long, long), EnumValue> EnumIndex { get; private set; } = new Dictionary<(long, long), EnumValue>();
    public long LastEmitUnixMs { get; private set; }
    public Metrics? Metrics { get; private set; }

    public void ApplyMeta(IReadOnlyList<ChannelMeta> channels, IReadOnlyList<EnumValue> enums)
    {
        // channels arrive pre-sorted by display_order; Sample.Values[i] aligns to channels[i].
        Channels = channels;
        EnumIndex = ValueFormat.BuildEnumIndex(enums);
        _idToIndex = [];
        _series.Clear();
        _lat.Clear();
        _lon.Clear();
        _lastGpsTs = 0;
        _latIdx = _lonIdx = -1;
        _latest = Array.Empty<double>();
        LastEmitUnixMs = 0;
        Metrics = null;
        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            _idToIndex[ch.Id] = i;
            // Buffer a time-series for every chartable widget — strip AND gauge — not just
            // strip, so a gauge toggled to a line chart in the interactive grid shows its
            // recent history instead of an empty plot.
            if (ch.Widget == "strip" || ch.Widget == "gauge")
            {
                _series[ch.Id] = new ChannelSeries(_windowMs);
            }

            if (ch.Widget == "map_lat")
            {
                _latIdx = i;
            }

            if (ch.Widget == "map_lon")
            {
                _lonIdx = i;
            }
        }
    }

    public void ApplyFrame(Sample s, long emitUnixMs)
    {
        if (s.Values.Length != Channels.Count)
        {
            return;
        }

        _latest = s.Values;
        LastEmitUnixMs = emitUnixMs;
        for (int i = 0; i < Channels.Count; i++)
        {
            if (_series.TryGetValue(Channels[i].Id, out var series))
            {
                series.Push(s.TsMs, s.Values[i]);
            }
        }

        // First point always stored (_lat empty guards against long underflow); thereafter only
        // when ts has advanced by the decimation interval.
        if (_latIdx >= 0 && _lonIdx >= 0 && (_lat.Count == 0 || s.TsMs - _lastGpsTs >= _gpsIntervalMs))
        {
            _lat.Add(s.Values[_latIdx]);
            _lon.Add(s.Values[_lonIdx]);
            _lastGpsTs = s.TsMs;
        }
    }

    public void ApplyMetrics(Metrics m) => Metrics = m;

    public double? Latest(long channelId)
        => _idToIndex.TryGetValue(channelId, out var i) && i < _latest.Length ? _latest[i] : null;

    public ChannelSeries? Series(long channelId)
        => _series.TryGetValue(channelId, out var s) ? s : null;

    public (IReadOnlyList<double> Lat, IReadOnlyList<double> Lon) GpsTrack() => (_lat, _lon);
}
