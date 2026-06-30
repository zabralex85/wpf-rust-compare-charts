namespace TelemetryPoc.Domain;

public sealed class TelemetryStore
{
    private readonly long _windowMs;
    private IReadOnlyList<ChannelMeta> _channels = Array.Empty<ChannelMeta>();
    private IReadOnlyDictionary<(long, long), EnumValue> _enumIndex =
        new Dictionary<(long, long), EnumValue>();
    private Dictionary<long, int> _idToIndex = new();
    private double[] _latest = Array.Empty<double>();
    private readonly Dictionary<long, ChannelSeries> _series = new();
    private int _latIdx = -1, _lonIdx = -1;
    private readonly List<double> _lat = new();
    private readonly List<double> _lon = new();
    private long _lastEmit;
    private Metrics? _metrics;

    public TelemetryStore(long windowMs = 60_000) => _windowMs = windowMs;

    public IReadOnlyList<ChannelMeta> Channels => _channels;
    public IReadOnlyDictionary<(long, long), EnumValue> EnumIndex => _enumIndex;
    public long LastEmitUnixMs => _lastEmit;
    public Metrics? Metrics => _metrics;

    public void ApplyMeta(IReadOnlyList<ChannelMeta> channels, IReadOnlyList<EnumValue> enums)
    {
        // channels arrive pre-sorted by display_order; Sample.Values[i] aligns to channels[i].
        _channels = channels;
        _enumIndex = ValueFormat.BuildEnumIndex(enums);
        _idToIndex = new Dictionary<long, int>();
        _series.Clear();
        _lat.Clear();
        _lon.Clear();
        _latIdx = _lonIdx = -1;
        _latest = Array.Empty<double>();
        _lastEmit = 0;
        _metrics = null;
        for (int i = 0; i < channels.Count; i++)
        {
            var ch = channels[i];
            _idToIndex[ch.Id] = i;
            if (ch.Widget == "strip") _series[ch.Id] = new ChannelSeries(_windowMs);
            if (ch.Widget == "map_lat") _latIdx = i;
            if (ch.Widget == "map_lon") _lonIdx = i;
        }
    }

    public void ApplyFrame(Sample s, long emitUnixMs)
    {
        if (s.Values.Length != _channels.Count) return;
        _latest = s.Values;
        _lastEmit = emitUnixMs;
        for (int i = 0; i < _channels.Count; i++)
            if (_series.TryGetValue(_channels[i].Id, out var series))
                series.Push(s.TsMs, s.Values[i]);
        if (_latIdx >= 0 && _lonIdx >= 0)
        {
            _lat.Add(s.Values[_latIdx]);
            _lon.Add(s.Values[_lonIdx]);
        }
    }

    public void ApplyMetrics(Metrics m) => _metrics = m;

    public double? Latest(long channelId)
        => _idToIndex.TryGetValue(channelId, out var i) && i < _latest.Length ? _latest[i] : null;

    public ChannelSeries? Series(long channelId)
        => _series.TryGetValue(channelId, out var s) ? s : null;

    public (IReadOnlyList<double> Lat, IReadOnlyList<double> Lon) GpsTrack() => (_lat, _lon);
}
