using Microsoft.Data.Sqlite;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Infrastructure;

/// <summary>Streams ride samples forward from SQLite over a live reader, reading one row
/// ahead so PeekTs is available without materialising the ride. Owns its own read-only
/// connection; SeekTo re-executes the query with a ts-lower-bound filter (indexed).</summary>
public sealed class SqliteSampleCursor : ISampleCursor
{
    private readonly string _colList;     // "ts, \"a\", \"b\""
    private readonly int _channelCount;
    private readonly SqliteConnection _conn;
    private SqliteCommand? _cmd;
    private SqliteDataReader? _reader;
    private Sample? _peek;

    public SqliteSampleCursor(string dbPath, IReadOnlyList<string> columns)
    {
        _channelCount = columns.Count;
        _colList = "ts, " + string.Join(", ", columns.Select(c => "\"" + c + "\""));
        _conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _conn.Open();
        Query(0);
    }

    public long? PeekTs => _peek?.TsMs;

    public Sample Read()
    {
        var s = _peek!;   // Sample is a record (reference type); non-null when PeekTs is non-null
        ReadAhead();
        return s;
    }

    public void SeekTo(long rideMs) => Query(rideMs);

    private void Query(long fromMs)
    {
        _reader?.Dispose();
        _cmd?.Dispose();
        _cmd = _conn.CreateCommand();
        _cmd.CommandText = $"SELECT {_colList} FROM samples WHERE ts >= $from ORDER BY ts";
        _cmd.Parameters.AddWithValue("$from", fromMs);
        _reader = _cmd.ExecuteReader();
        ReadAhead();
    }

    private void ReadAhead()
    {
        if (_reader is not null && _reader.Read())
        {
            var values = new double[_channelCount];
            for (int i = 0; i < _channelCount; i++)
            {
                values[i] = _reader.GetDouble(i + 1);
            }

            _peek = new Sample(_reader.GetInt64(0), values);
        }
        else
        {
            _peek = null;
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _cmd?.Dispose();
        _conn.Dispose();
    }
}
