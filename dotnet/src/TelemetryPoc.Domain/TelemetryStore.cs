namespace TelemetryPoc.Domain;

public sealed class TelemetryStore
{
    private readonly long _windowMs;
    private Dictionary<long, int> _idToIndex = [];
    private double[] _latest = Array.Empty<double>();
    private readonly Dictionary<long, ChannelSeries> _series = [];
    private int _latIdx = -1, _lonIdx = -1;
    private readonly List<double> _lat = [];
    private readonly List<double> _lon = [];

    public TelemetryStore(long windowMs = 60_000)
    {
        _windowMs = windowMs;
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

        if (_latIdx >= 0 && _lonIdx >= 0)
        {
            _lat.Add(s.Values[_latIdx]);
            _lon.Add(s.Values[_lonIdx]);
        }
    }

    public void ApplyMetrics(Metrics m) => Metrics = m;

    public double? Latest(long channelId)
        => _idToIndex.TryGetValue(channelId, out var i) && i < _latest.Length ? _latest[i] : null;

    public ChannelSeries? Series(long channelId)
        => _series.TryGetValue(channelId, out var s) ? s : null;

    public (IReadOnlyList<double> Lat, IReadOnlyList<double> Lon) GpsTrack() => (_lat, _lon);
}
