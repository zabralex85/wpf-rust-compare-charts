# Ride Streaming (.NET) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replay ride samples by streaming them forward from SQLite via an `ISampleCursor` instead of loading the entire ride into a `List<Sample>`, cutting ~80 MB on a 12 h ride — with zero behavior change.

**Architecture:** Onion rings (Domain/Application/Infrastructure/App). The Application gains an `ISampleCursor` port; `ReplayPlayer` consumes it instead of a list; Infrastructure's `SqliteRideSource` loads only metadata + GPS bounds (via a `MIN/MAX` SQL) and opens a `SqliteSampleCursor` over a live `DbDataReader`. `RideEngine` owns and disposes the cursor.

**Tech Stack:** .NET 8, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Target frameworks unchanged: `net8.0` libs, `net8.0-windows` for `TelemetryPoc.App`.
- **Zero behavior change**: identical frame sequence, timings, transport, seek, map, HUD. Replay parity is the hard requirement.
- Every task ends green for the project it touches: `dotnet build <project> -c Debug --nologo` → 0 warnings / 0 errors (StyleCop active; warnings fail). The whole solution + `dotnet test` go green at Task 6.
- Solution file: `dotnet/TelemetryPoc.slnx`.
- 4-space indent, file-scoped namespaces, CRLF (enforced by `.editorconfig`/`.gitattributes`).
- The `samples` table has `ts INTEGER PRIMARY KEY`, so `WHERE ts >= @t ORDER BY ts` is an index seek.
- `Sample.Values` is index-aligned to channels sorted by `display_order`; the cursor's `SELECT ts, <cols>` must use the same column order (`SqliteRideSource.LoadChannels` returns `ORDER BY display_order`).

---

## File Structure

- `dotnet/src/TelemetryPoc.Application/Ports.cs` — add `ISampleCursor`; add `OpenSamples()` to `IRideSource`.
- `dotnet/src/TelemetryPoc.Application/RideData.cs` — drop the `Samples` member.
- `dotnet/src/TelemetryPoc.Application/ReplayPlayer.cs` — consume `ISampleCursor`.
- `dotnet/src/TelemetryPoc.Application/RideEngine.cs` — take + own (dispose) the cursor; `: IDisposable`.
- `dotnet/src/TelemetryPoc.Infrastructure/SqliteRideSource.cs` — meta-only `Load`; GPS bounds via SQL; `OpenSamples()`.
- `dotnet/src/TelemetryPoc.Infrastructure/SqliteSampleCursor.cs` — new; the streaming cursor.
- `dotnet/src/TelemetryPoc.App/RideSession.cs` — open the cursor, build the engine, dispose it.
- `dotnet/tests/TelemetryPoc.Tests/FakeSampleCursor.cs` — new; in-memory cursor for tests.
- `dotnet/tests/TelemetryPoc.Tests/ReplayPlayerTests.cs`, `RideEngineTests.cs`, `SampleReaderTests.cs` — updated.
- `dotnet/tests/TelemetryPoc.Tests/SqliteSampleCursorTests.cs` — new.

---

## Task 1: Add the `ISampleCursor` port and `IRideSource.OpenSamples`

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Application/Ports.cs`

**Interfaces:**
- Produces: `ISampleCursor : IDisposable` with `long? PeekTs { get; }`, `Sample Read()`, `void SeekTo(long rideMs)`; and `IRideSource.OpenSamples() : ISampleCursor`.

- [ ] **Step 1: Add the port + method**

In `Ports.cs`, change the `IRideSource` doc/signature and add `ISampleCursor`:

```csharp
/// <summary>Loads ride metadata (channels, enums, duration, GPS bounds) and opens a
/// forward sample cursor. Samples are streamed, never fully materialised.</summary>
public interface IRideSource
{
    Task<RideData> LoadAsync(CancellationToken ct = default);

    /// <summary>Open a fresh forward cursor over the ride's samples (ts-ascending).
    /// The caller owns and disposes it.</summary>
    ISampleCursor OpenSamples();
}

/// <summary>Forward, seekable cursor over ride samples — streamed from storage so the
/// whole ride is never held in memory.</summary>
public interface ISampleCursor : IDisposable
{
    /// <summary>TsMs of the row at the playhead, or null once past the last row.</summary>
    long? PeekTs { get; }

    /// <summary>Return the row at the playhead and advance one. Call only when PeekTs is non-null.</summary>
    Sample Read();

    /// <summary>Reposition the playhead to the first row with TsMs &gt;= rideMs.</summary>
    void SeekTo(long rideMs);
}
```

- [ ] **Step 2: Build Application (will error — IRideSource impl is incomplete; that's expected this task)**

Run: `dotnet build dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj -c Debug --nologo`
Expected: builds (the interface change alone compiles; `SqliteRideSource` is in another project and isn't built here). If it builds 0/0, proceed; the Infrastructure impl is Task 4.

- [ ] **Step 3: Commit**

```bash
git add dotnet/src/TelemetryPoc.Application/Ports.cs
git commit -m "feat(dotnet): add ISampleCursor port + IRideSource.OpenSamples"
```

---

## Task 2: `ReplayPlayer` consumes `ISampleCursor`

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Application/ReplayPlayer.cs`
- Create: `dotnet/tests/TelemetryPoc.Tests/FakeSampleCursor.cs`
- Modify: `dotnet/tests/TelemetryPoc.Tests/ReplayPlayerTests.cs`

**Interfaces:**
- Consumes: `ISampleCursor` (Task 1).
- Produces: `ReplayPlayer(ISampleCursor cursor, TelemetryStore store)`; `bool Done`, `long? PeekTs`, `int Advance(long rideMs, long nowUnixMs)`, `void SeekTo(long rideMs)`. `FakeSampleCursor(IReadOnlyList<Sample>)`.

- [ ] **Step 1: Write the FakeSampleCursor test helper**

```csharp
// dotnet/tests/TelemetryPoc.Tests/FakeSampleCursor.cs
using System.Collections.Generic;
using TelemetryPoc.Application;
using TelemetryPoc.Domain;

namespace TelemetryPoc.Tests;

/// <summary>In-memory ISampleCursor for tests — mirrors the old list-backed player so
/// parity assertions hold without touching SQLite.</summary>
internal sealed class FakeSampleCursor : ISampleCursor
{
    private readonly IReadOnlyList<Sample> _samples;
    private int _next;

    public FakeSampleCursor(IReadOnlyList<Sample> samples) => _samples = samples;

    public long? PeekTs => _next < _samples.Count ? _samples[_next].TsMs : null;

    public Sample Read() => _samples[_next++];

    public void SeekTo(long rideMs)
    {
        int lo = 0, hi = _samples.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (_samples[mid].TsMs < rideMs) lo = mid + 1;
            else hi = mid;
        }
        _next = lo;
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Rewrite ReplayPlayer**

```csharp
// dotnet/src/TelemetryPoc.Application/ReplayPlayer.cs
using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Drives the store from a forward sample cursor: each Advance applies every
/// row now due (ts &lt;= rideMs). The cursor streams rows, so the whole ride is never in memory.</summary>
public sealed class ReplayPlayer
{
    private readonly ISampleCursor _cursor;
    private readonly TelemetryStore _store;

    public ReplayPlayer(ISampleCursor cursor, TelemetryStore store)
    {
        _cursor = cursor;
        _store = store;
    }

    public bool Done => _cursor.PeekTs is null;

    public long? PeekTs => _cursor.PeekTs;

    /// <summary>Apply every sample whose TsMs is at or before the ride clock. Returns the count applied.</summary>
    public int Advance(long rideMs, long nowUnixMs)
    {
        int applied = 0;
        while (_cursor.PeekTs is { } t && t <= rideMs)
        {
            _store.ApplyFrame(_cursor.Read(), nowUnixMs);
            applied++;
        }
        return applied;
    }

    /// <summary>Reposition to the first sample with TsMs &gt;= rideMs.</summary>
    public void SeekTo(long rideMs) => _cursor.SeekTo(rideMs);
}
```

- [ ] **Step 3: Update ReplayPlayerTests to the cursor**

Replace `NewPlayer()` and the two `SeekTo` assertions (SeekTo is now `void`):

```csharp
    private static (ReplayPlayer, TelemetryStore) NewPlayer()
    {
        var (ch, ev, samples) = Setup();
        var store = new TelemetryStore();
        store.ApplyMeta(ch, ev);
        return (new ReplayPlayer(new FakeSampleCursor(samples), store), store);
    }
```

Then change the three `SeekTo` tests to call the void method and assert via `PeekTs`/`Done`:

```csharp
    [Fact]
    public void SeekTo_lands_on_first_sample_at_or_after_target()
    {
        var (player, _) = NewPlayer();
        player.SeekTo(50);                 // first ts >= 50 is ts=100
        Assert.Equal(100, player.PeekTs);
    }

    [Fact]
    public void SeekTo_before_first_is_index_zero()
    {
        var (player, _) = NewPlayer();
        player.SeekTo(-10);
        Assert.Equal(0, player.PeekTs);
    }

    [Fact]
    public void SeekTo_past_last_is_count_and_done()
    {
        var (player, _) = NewPlayer();
        player.SeekTo(999);
        Assert.True(player.Done);
        Assert.Null(player.PeekTs);
    }
```

(The `Advance_*` and `Advance_after_SeekTo_*` tests stay as-is — but in
`Advance_after_SeekTo_applies_from_new_index` change `player.SeekTo(200);` — it already
returns void there, no edit needed beyond NewPlayer.)

- [ ] **Step 4: Run ReplayPlayer tests**

Run: `dotnet test dotnet/tests/TelemetryPoc.Tests/TelemetryPoc.Tests.csproj -c Debug --filter "FullyQualifiedName~ReplayPlayerTests"`
Expected: PASS (the whole-solution build still fails because RideEngine/SqliteRideSource aren't updated yet — that's fine; run with `--filter` after a project build of Application, or expect the test project build to fail until Task 5. If the test project won't build, skip running here and rely on Task 6's full run; still commit.)

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Application/ReplayPlayer.cs dotnet/tests/TelemetryPoc.Tests/FakeSampleCursor.cs dotnet/tests/TelemetryPoc.Tests/ReplayPlayerTests.cs
git commit -m "feat(dotnet): ReplayPlayer streams from ISampleCursor (+ fake cursor tests)"
```

---

## Task 3: `RideData` drops samples; `RideEngine` owns the cursor

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Application/RideData.cs`
- Modify: `dotnet/src/TelemetryPoc.Application/RideEngine.cs`
- Modify: `dotnet/tests/TelemetryPoc.Tests/RideEngineTests.cs`

**Interfaces:**
- Consumes: `ISampleCursor`, `ReplayPlayer(ISampleCursor, TelemetryStore)`.
- Produces: `RideData(Channels, Enums, DurationMs, GpsBounds)` (no Samples); `RideEngine(RideData data, ISampleCursor cursor, TelemetryStore store, IMetricsSampler metrics) : IDisposable`.

- [ ] **Step 1: Drop Samples from RideData**

```csharp
// dotnet/src/TelemetryPoc.Application/RideData.cs
using TelemetryPoc.Domain;

namespace TelemetryPoc.Application;

/// <summary>Ride metadata, ready to replay. Samples are streamed via ISampleCursor, not
/// carried here, so a large ride costs no extra memory to "load".</summary>
public sealed record RideData(
    IReadOnlyList<ChannelMeta> Channels,
    IReadOnlyList<EnumValue> Enums,
    long DurationMs,
    (double MinLat, double MinLon, double MaxLat, double MaxLon)? GpsBounds);
```

- [ ] **Step 2: RideEngine takes + disposes the cursor**

Change the field block, ctor, and add `IDisposable`:

```csharp
public sealed class RideEngine : IDisposable
{
    private readonly RideData _data;
    private readonly RideClock _clock = new();
    private readonly IMetricsSampler _metrics;
    private readonly ISampleCursor _cursor;
    private readonly ReplayPlayer _player;
    private long _lastMetricSec = -1;
    // ... (Store, DurationMs, RideMs, GpsBounds, IsPaused, Reset unchanged) ...

    public RideEngine(RideData data, ISampleCursor cursor, TelemetryStore store, IMetricsSampler metrics)
    {
        _data = data;
        _cursor = cursor;
        Store = store;
        _metrics = metrics;
        Store.ApplyMeta(data.Channels, data.Enums);
        _player = new ReplayPlayer(cursor, Store);
    }

    // ... Advance / Pause / Resume / Seek unchanged (Seek already calls _player.SeekTo then _player.PeekTs) ...

    public void Dispose() => _cursor.Dispose();
}
```

(`Seek` already does `_player.SeekTo(target); var snapped = _player.PeekTs ?? target;` — that
still compiles since `SeekTo` is now void and `PeekTs` is read after.)

- [ ] **Step 3: Update RideEngineTests (fake cursor; RideData has no samples)**

```csharp
    private static RideData Ride() =>
        new(
            new ChannelMeta[]
            {
                new(1, "A", "a", "u", "f", 0, 100, "strip", 0, "I_01"),
                new(2, "B", "b", "u", "f", 0, 100, "gauge", 1, "I_02"),
            },
            [],
            DurationMs: 1000,
            GpsBounds: (1, 2, 3, 4));

    // 6 samples at 0,100,...,500 ms — fed through a fake cursor.
    private static FakeSampleCursor Cursor()
    {
        var samples = new List<Sample>();
        for (int i = 0; i <= 5; i++) samples.Add(new Sample(i * 100, new double[] { i, i * 2 }));
        return new FakeSampleCursor(samples);
    }

    private static RideEngine NewEngine() => new(Ride(), Cursor(), new TelemetryStore(), new FakeMetrics());
```

The existing assertions (`Ctor_applies_meta…`, `Advance_*`, `Seek_*`, `Paused_*`) are unchanged.

- [ ] **Step 4: Build Application**

Run: `dotnet build dotnet/src/TelemetryPoc.Application/TelemetryPoc.Application.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Application/RideData.cs dotnet/src/TelemetryPoc.Application/RideEngine.cs dotnet/tests/TelemetryPoc.Tests/RideEngineTests.cs
git commit -m "feat(dotnet): RideData drops samples; RideEngine owns+disposes the cursor"
```

---

## Task 4: Infrastructure — meta-only load, SQL GPS bounds, `SqliteSampleCursor`

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Infrastructure/SqliteRideSource.cs`
- Create: `dotnet/src/TelemetryPoc.Infrastructure/SqliteSampleCursor.cs`
- Modify: `dotnet/tests/TelemetryPoc.Tests/SampleReaderTests.cs`
- Create: `dotnet/tests/TelemetryPoc.Tests/SqliteSampleCursorTests.cs`

**Interfaces:**
- Consumes: `IRideSource`, `ISampleCursor`, `IRidePathResolver`, `MapProject.TrackBounds`.
- Produces: `SqliteRideSource.OpenSamples()`; `SqliteSampleCursor(string dbPath, IReadOnlyList<string> columns)`. `LoadSamples` stays a `public static` test helper (production `Load` no longer calls it).

- [ ] **Step 1: Write SqliteSampleCursor**

```csharp
// dotnet/src/TelemetryPoc.Infrastructure/SqliteSampleCursor.cs
using System.Collections.Generic;
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
    private SqliteDataReader? _reader;
    private Sample? _peek;

    public SqliteSampleCursor(string dbPath, IReadOnlyList<string> columns)
    {
        _channelCount = columns.Count;
        _colList = "ts, " + string.Join(", ", columns.ConvertAll(c => "\"" + c + "\""));
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
        var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {_colList} FROM samples WHERE ts >= $from ORDER BY ts";
        cmd.Parameters.AddWithValue("$from", fromMs);
        _reader = cmd.ExecuteReader();
        ReadAhead();
    }

    private void ReadAhead()
    {
        if (_reader is not null && _reader.Read())
        {
            var values = new double[_channelCount];
            for (int i = 0; i < _channelCount; i++) values[i] = _reader.GetDouble(i + 1);
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
        _conn.Dispose();
    }
}
```

> `Sample` is `public sealed record Sample(long TsMs, double[] Values)` in `dotnet/src/TelemetryPoc.Domain/Models.cs` — a reference type. `private Sample? _peek;` is a nullable reference and `Read()` returns `_peek!` (as written above). Do not use `.Value`.

- [ ] **Step 2: Rewrite SqliteRideSource.Load (meta + SQL bounds) and add OpenSamples**

Replace `Load` and `GpsBounds`; add `OpenSamples`. Keep `LoadChannels`/`LoadEnumValues`/`LoadRideMeta`/`LoadSamples` (the last as a test helper):

```csharp
    public ISampleCursor OpenSamples()
    {
        var dbPath = _paths.ResolveRideDb();
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var columns = new List<string>();
        foreach (var c in LoadChannels(conn)) columns.Add(c.ColumnName);
        return new SqliteSampleCursor(dbPath, columns);
    }

    private static RideData Load(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        var channels = LoadChannels(conn);
        var enums = LoadEnumValues(conn);
        var meta = LoadRideMeta(conn);
        return new RideData(channels, enums, meta.DurationS * 1000, GpsBounds(conn, channels));
    }

    /// <summary>Whole-ride GPS bbox via a MIN/MAX aggregate — no sample materialisation.</summary>
    private static (double, double, double, double)? GpsBounds(SqliteConnection conn, IReadOnlyList<ChannelMeta> channels)
    {
        string? latCol = null, lonCol = null;
        foreach (var c in channels)
        {
            if (c.Widget == "map_lat") latCol = c.ColumnName;
            if (c.Widget == "map_lon") lonCol = c.ColumnName;
        }
        if (latCol is null || lonCol is null) return null;

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT MIN(\"{latCol}\"), MIN(\"{lonCol}\"), MAX(\"{latCol}\"), MAX(\"{lonCol}\") FROM samples";
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return null; // empty samples table
        return (r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3));
    }
```

> Bounds parity: the old in-memory path used `MapProject.TrackBounds` = min/max of lat and lon independently, which is exactly what these `MIN/MAX` columns produce. Keep the result tuple order `(MinLat, MinLon, MaxLat, MaxLon)`.

- [ ] **Step 3: Add SqliteSampleCursorTests + adjust SampleReaderTests**

`SampleReaderTests` keeps exercising the `LoadSamples` helper (unchanged). Add a new file:

```csharp
// dotnet/tests/TelemetryPoc.Tests/SqliteSampleCursorTests.cs
using System.Collections.Generic;
using TelemetryPoc.Infrastructure;
using Xunit;

namespace TelemetryPoc.Tests;

public class SqliteSampleCursorTests
{
    private static List<string> Cols()
    {
        using var conn = Fixtures.Open();
        var cols = new List<string>();
        foreach (var c in SqliteRideSource.LoadChannels(conn)) cols.Add(c.ColumnName);
        return cols;
    }

    [Fact]
    public void Streams_rows_in_ts_order_then_ends()
    {
        using var cur = new SqliteSampleCursor(Fixtures.RideSmallDb(), Cols());
        Assert.Equal(0, cur.PeekTs);
        long last = -1; int n = 0;
        while (cur.PeekTs is { } t)
        {
            Assert.True(t > last);
            var s = cur.Read();
            Assert.Equal(t, s.TsMs);
            last = t; n++;
        }
        Assert.Equal(100, n);          // ride_small.db has 100 samples
        Assert.Null(cur.PeekTs);
    }

    [Fact]
    public void SeekTo_lands_on_first_row_at_or_after_target()
    {
        using var cur = new SqliteSampleCursor(Fixtures.RideSmallDb(), Cols());
        cur.SeekTo(150);               // rows at 0,100,200,...; first >= 150 is 200
        Assert.Equal(200, cur.PeekTs);
    }

    [Fact]
    public void SeekTo_past_end_is_null()
    {
        using var cur = new SqliteSampleCursor(Fixtures.RideSmallDb(), Cols());
        cur.SeekTo(1_000_000);
        Assert.Null(cur.PeekTs);
    }
}
```

> `Fixtures.RideSmallDb()` returns the path; `Fixtures.Open()` opens a connection (both already exist in `Fixtures.cs`).

- [ ] **Step 4: Build Infrastructure**

Run: `dotnet build dotnet/src/TelemetryPoc.Infrastructure/TelemetryPoc.Infrastructure.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Infrastructure dotnet/tests/TelemetryPoc.Tests/SqliteSampleCursorTests.cs
git commit -m "feat(dotnet): SqliteSampleCursor + meta-only load with SQL GPS bounds"
```

---

## Task 5: Wire the cursor into `RideSession`

**Files:**
- Modify: `dotnet/src/TelemetryPoc.App/RideSession.cs`

**Interfaces:**
- Consumes: `IRideSource.LoadAsync` + `OpenSamples()`, `RideEngine(RideData, ISampleCursor, TelemetryStore, IMetricsSampler)`.

- [ ] **Step 1: Open the cursor, build the engine, dispose it**

In `StartAsync`, after the load, open the cursor and pass it; fix the log line (no `data.Samples`):

```csharp
            var data = await _source.LoadAsync().ConfigureAwait(true); // resume on UI thread
            var cursor = _source.OpenSamples();
            _log.LogInformation("Ride loaded: {Channels} channels, {DurationMs} ms", data.Channels.Count, data.DurationMs);

            _engine = new RideEngine(data, cursor, Store, _metrics);
            _engine.Reset += () => Reset?.Invoke();
```

In `Dispose`, dispose the engine (which disposes the cursor) before nulling the timer:

```csharp
    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        _engine?.Dispose();
    }
```

- [ ] **Step 2: Build the App project (transitively builds all rings)**

Run: `dotnet build dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add dotnet/src/TelemetryPoc.App/RideSession.cs
git commit -m "feat(dotnet): RideSession opens the streaming cursor + disposes the engine"
```

---

## Task 6: Full suite, launch parity, and 12 h memory re-measure

**Files:** none (verification + a README note).

- [ ] **Step 1: Whole-solution build + test**

Run: `dotnet build dotnet/TelemetryPoc.slnx -c Debug --nologo && dotnet test dotnet/TelemetryPoc.slnx -c Debug --no-build`
Expected: `0 Warning(s) 0 Error(s)` and all tests pass (171 baseline + the new `SqliteSampleCursorTests`, minus none removed). If any test fails, it is a parity break — fix the cursor, do not change the assertion's expected ride behavior.

- [ ] **Step 2: Launch parity check (10-min ride)**

Run: `dotnet run --project dotnet/src/TelemetryPoc.App -c Debug` (appsettings drives Speed 5, walk-up finds `data/ride.db` + tiles). Confirm the dashboard replays identically — params, gauges, charts, map + GPS track, transport advancing — then close.

- [ ] **Step 3: 12 h-ride memory re-measure**

Generate (if absent) and measure with the streaming build:

```bash
python data/simulate.py --out data/ride_12h.db   # 432000 samples (~13s)
```

Build Release and run with `RIDE_DB` pointing at the 12 h DB, 30 s warm-up, then read `WorkingSet64`:

```powershell
dotnet build dotnet/src/TelemetryPoc.App/TelemetryPoc.App.csproj -c Release --nologo
$env:RIDE_DB="<repo>\data\ride_12h.db"; $env:RIDE_MBTILES="<repo>\tiles\israel.mbtiles"; $env:RIDE_SPEED="5"
Start-Process "<repo>\dotnet\src\TelemetryPoc.App\bin\Release\net8.0-windows\TelemetryPoc.App.exe"
Start-Sleep 30; '{0:F0} MB' -f ((Get-Process TelemetryPoc.App).WorkingSet64/1MB)
Get-Process TelemetryPoc.App | Stop-Process -Force
```

Expected: working set ~**253 MB** (down from the ~333 MB load-all baseline — the ~80 MB sample load is gone). Record the number.

- [ ] **Step 4: README note**

In `dotnet/README.md`, under the architecture/notes, add one line: the ride is **streamed** from SQLite (forward `ISampleCursor`), so memory stays flat regardless of ride length.

- [ ] **Step 5: Commit**

```bash
git add dotnet/README.md
git commit -m "docs(dotnet): note ride samples are streamed from SQLite (flat memory)"
```

---

## Self-Review Notes (addressed)

- **Spec coverage:** RideData drop (T3), ISampleCursor + OpenSamples (T1), ReplayPlayer on cursor (T2), SqliteRideSource meta+MIN/MAX bounds + OpenSamples + SqliteSampleCursor (T4), RideEngine IDisposable owning cursor (T3), RideSession wiring (T5), fake-cursor parity + SqliteSampleCursorTests (T2/T3/T4), 12 h re-measure (T6). All spec items mapped.
- **Type consistency:** `ISampleCursor { long? PeekTs; Sample Read(); void SeekTo(long); }` used identically in `ReplayPlayer`, `FakeSampleCursor`, `SqliteSampleCursor`. `RideEngine(RideData, ISampleCursor, TelemetryStore, IMetricsSampler)` matches the `RideSession` call. `OpenSamples()` returns `ISampleCursor`.
- **Parity:** the cursor yields rows in `ts` order; `ReplayPlayer.Advance` applies `ts <= rideMs` exactly as the list version; `SeekTo` lands on first `ts >= rideMs` (binary search in the fake, `WHERE ts >= @t` in SQLite) — identical semantics to the old binary-search index.
- **`Sample` value vs reference:** `Sample` is a `record` (reference type) — `SqliteSampleCursor` uses `private Sample? _peek;` (nullable ref) and `Read()` returns `_peek!`; the Task 4 note flags this so the implementer doesn't write `.Value`.
