# .NET Core Data Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure C# data/logic layer for the .NET dashboard: read the shared `ride.db`, pace replay deterministically, buffer time-series, maintain telemetry state, and provide value/enum formatting + process metrics — all xUnit-tested, no UI.

**Architecture:** A `net8.0` class library `TelemetryPoc.Core` under `dotnet/`, split into focused types: models (`ChannelMeta`/`EnumValue`/`Sample`/`RideMeta`), `TelemetryDb` (Microsoft.Data.Sqlite reader), `Pacer` (deterministic pacing), `ValueFormat` (formatting + enum), `MetricsSampler` (process CPU/RAM), `ChannelSeries` (windowed buffer), and `TelemetryStore` (state aggregation). The WPF + Blazor Hybrid UI (a later plan) feeds the store from an in-process replay loop and renders with ScottPlot/Leaflet.

**Tech Stack:** .NET 8 (LTS), C# (nullable enabled), Microsoft.Data.Sqlite, xUnit. Tests run against the committed `data/ride_small.db` fixture.

## Global Constraints

- Reads the Plan-1 DB contract read-only. Tables: `channels(id, name, column_name, unit, type, min, max, widget, display_order, addr)`, `enum_values(channel_id, code, label, severity)`, wide `samples(ts INTEGER PRIMARY KEY, <one col per channel in display order>)`, `ride_meta(start_time, duration_s, rate_hz, channel_count)`.
- `ts` is integer **milliseconds** from ride start; sample `Values` are index-aligned to `channels` ordered by `display_order`; every value read as `double` (enum/int columns coerce via `Convert.ToDouble`).
- Latency model mirrors the Rust app: the store records an `emitUnixMs` per frame; the UI computes `now - emitUnixMs`.
- Strip-chart window default = **60_000 ms**.
- `<Nullable>enable</Nullable>`; no nullable-warning suppressions. Formatting uses `CultureInfo.InvariantCulture` for deterministic output.
- Fixture for all tests: `data/ride_small.db` (located by walking up from the test assembly directory). 100 rows, 30 channels, 10 Hz, ≥1 `inu_mode2=1` enum event.
- Target framework `net8.0` for the core lib and tests (no `-windows` suffix — the core has no UI deps).

---

### Task 1: Scaffold solution + core lib + test project

**Files:**
- Create: `dotnet/TelemetryPoc.sln`
- Create: `dotnet/src/TelemetryPoc.Core/TelemetryPoc.Core.csproj`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryPoc.Core.Tests.csproj`
- Create: `dotnet/.gitignore`

**Interfaces:**
- Produces: a solution where `dotnet test` builds and runs (0 tests initially). Core lib references Microsoft.Data.Sqlite; test project references xUnit + the core lib.

- [ ] **Step 1: Create the projects**

Run (from repo root):
```bash
mkdir -p dotnet && cd dotnet
dotnet new sln -n TelemetryPoc
dotnet new classlib -n TelemetryPoc.Core -o src/TelemetryPoc.Core -f net8.0
dotnet new xunit -n TelemetryPoc.Core.Tests -o tests/TelemetryPoc.Core.Tests -f net8.0
dotnet sln add src/TelemetryPoc.Core tests/TelemetryPoc.Core.Tests
dotnet add tests/TelemetryPoc.Core.Tests reference src/TelemetryPoc.Core
dotnet add src/TelemetryPoc.Core package Microsoft.Data.Sqlite --version 8.0.8
rm src/TelemetryPoc.Core/Class1.cs tests/TelemetryPoc.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Enable nullable + invariant globalization in both csproj**

Ensure each `.csproj` `<PropertyGroup>` has `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` (the templates set these by default for net8.0 — confirm; add if missing).

- [ ] **Step 3: Verify the solution builds and tests run**

Run (from `dotnet/`): `dotnet test`
Expected: build succeeds; `Passed! - Failed: 0, Passed: 0` (no tests yet).

- [ ] **Step 4: Add dotnet/.gitignore**

```
# dotnet/.gitignore
bin/
obj/
*.user
```

- [ ] **Step 5: Commit**

```bash
git add dotnet/
git commit -m "feat(dotnet): scaffold TelemetryPoc.Core solution + xunit tests"
```

---

### Task 2: Models + metadata reader

**Files:**
- Create: `dotnet/src/TelemetryPoc.Core/Models.cs`
- Create: `dotnet/src/TelemetryPoc.Core/TelemetryDb.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/Fixtures.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryDbTests.cs`

**Interfaces:**
- Produces:
  - `record ChannelMeta(long Id, string Name, string ColumnName, string Unit, string Type, double Min, double Max, string Widget, long DisplayOrder, string Addr)`
  - `record EnumValue(long ChannelId, long Code, string Label, string Severity)`
  - `record RideMeta(long StartTime, long DurationS, long RateHz, long ChannelCount)`
  - `record Sample(long TsMs, double[] Values)` (used in Task 3)
  - `TelemetryDb.LoadChannels(SqliteConnection) -> IReadOnlyList<ChannelMeta>` (ordered by display_order)
  - `TelemetryDb.LoadEnumValues(SqliteConnection) -> IReadOnlyList<EnumValue>`
  - `TelemetryDb.LoadRideMeta(SqliteConnection) -> RideMeta`
  - `Fixtures.RideSmallDb() -> string`, `Fixtures.Open() -> SqliteConnection`

- [ ] **Step 1: Write the failing test + fixture helper**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/Fixtures.cs
using Microsoft.Data.Sqlite;

namespace TelemetryPoc.Core.Tests;

public static class Fixtures
{
    public static string RideSmallDb()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "data", "ride_small.db");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("ride_small.db not found walking up from " + AppContext.BaseDirectory);
    }

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={RideSmallDb()};Mode=ReadOnly");
        conn.Open();
        return conn;
    }
}
```

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/TelemetryDbTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class TelemetryDbTests
{
    [Fact]
    public void LoadChannels_returns_thirty_in_display_order()
    {
        using var conn = Fixtures.Open();
        var chans = TelemetryDb.LoadChannels(conn);
        Assert.Equal(30, chans.Count);
        Assert.Equal(1, chans[0].Id);
        var orders = chans.Select(c => c.DisplayOrder).ToList();
        Assert.Equal(orders.OrderBy(x => x).ToList(), orders);
    }

    [Fact]
    public void LoadEnumValues_includes_inu_mode2_labels()
    {
        using var conn = Fixtures.Open();
        var evs = TelemetryDb.LoadEnumValues(conn);
        var labels = evs.Select(e => e.Label).ToList();
        Assert.Contains("Normal", labels);
        Assert.Contains("Critical", labels);
    }

    [Fact]
    public void LoadRideMeta_reports_rate_and_channel_count()
    {
        using var conn = Fixtures.Open();
        var meta = TelemetryDb.LoadRideMeta(conn);
        Assert.Equal(10, meta.RateHz);
        Assert.Equal(30, meta.ChannelCount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter TelemetryDbTests`
Expected: compile error — `TelemetryDb` / `ChannelMeta` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// dotnet/src/TelemetryPoc.Core/Models.cs
namespace TelemetryPoc.Core;

public sealed record ChannelMeta(
    long Id, string Name, string ColumnName, string Unit, string Type,
    double Min, double Max, string Widget, long DisplayOrder, string Addr);

public sealed record EnumValue(long ChannelId, long Code, string Label, string Severity);

public sealed record RideMeta(long StartTime, long DurationS, long RateHz, long ChannelCount);

public sealed record Sample(long TsMs, double[] Values);
```

```csharp
// dotnet/src/TelemetryPoc.Core/TelemetryDb.cs
using Microsoft.Data.Sqlite;

namespace TelemetryPoc.Core;

public static class TelemetryDb
{
    public static IReadOnlyList<ChannelMeta> LoadChannels(SqliteConnection conn)
    {
        var list = new List<ChannelMeta>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, name, column_name, unit, type, min, max, widget, display_order, addr " +
            "FROM channels ORDER BY display_order";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ChannelMeta(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetDouble(5), r.GetDouble(6), r.GetString(7), r.GetInt64(8), r.GetString(9)));
        }
        return list;
    }

    public static IReadOnlyList<EnumValue> LoadEnumValues(SqliteConnection conn)
    {
        var list = new List<EnumValue>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT channel_id, code, label, severity FROM enum_values ORDER BY channel_id, code";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EnumValue(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    public static RideMeta LoadRideMeta(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT start_time, duration_s, rate_hz, channel_count FROM ride_meta LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("ride_meta is empty");
        return new RideMeta(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `dotnet/`): `dotnet test --filter TelemetryDbTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/Models.cs dotnet/src/TelemetryPoc.Core/TelemetryDb.cs dotnet/tests/TelemetryPoc.Core.Tests/Fixtures.cs dotnet/tests/TelemetryPoc.Core.Tests/TelemetryDbTests.cs
git commit -m "feat(dotnet): models + channel/enum/ride metadata reader"
```

---

### Task 3: Sample reader

**Files:**
- Modify: `dotnet/src/TelemetryPoc.Core/TelemetryDb.cs` (add `LoadSamples`)
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/SampleReaderTests.cs`

**Interfaces:**
- Produces: `TelemetryDb.LoadSamples(SqliteConnection conn, IReadOnlyList<ChannelMeta> channels) -> IReadOnlyList<Sample>` — one `Sample` per row, ordered by `ts`, `Values` in `channels` order; each value `Convert.ToDouble(reader.GetValue(...))` (coerces INTEGER/enum columns). Identifier names are quoted in the SQL.

- [ ] **Step 1: Write the failing test**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/SampleReaderTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class SampleReaderTests
{
    [Fact]
    public void LoadSamples_returns_rows_in_ts_order_with_one_value_per_channel()
    {
        using var conn = Fixtures.Open();
        var chans = TelemetryDb.LoadChannels(conn);
        var samples = TelemetryDb.LoadSamples(conn, chans);

        Assert.Equal(100, samples.Count);
        Assert.Equal(0, samples[0].TsMs);
        Assert.Equal(100, samples[1].TsMs);
        Assert.All(samples, s => Assert.Equal(chans.Count, s.Values.Length));
        for (int i = 1; i < samples.Count; i++)
            Assert.True(samples[i].TsMs > samples[i - 1].TsMs);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter SampleReaderTests`
Expected: compile error — `LoadSamples` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `TelemetryDb.cs`:
```csharp
    public static IReadOnlyList<Sample> LoadSamples(SqliteConnection conn, IReadOnlyList<ChannelMeta> channels)
    {
        var cols = string.Join(", ", channels.Select(c => "\"" + c.ColumnName + "\""));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT ts, {cols} FROM samples ORDER BY ts";
        using var r = cmd.ExecuteReader();
        var list = new List<Sample>();
        var n = channels.Count;
        while (r.Read())
        {
            var values = new double[n];
            for (int i = 0; i < n; i++)
                values[i] = Convert.ToDouble(r.GetValue(i + 1));
            list.Add(new Sample(r.GetInt64(0), values));
        }
        return list;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `dotnet/`): `dotnet test --filter SampleReaderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/TelemetryDb.cs dotnet/tests/TelemetryPoc.Core.Tests/SampleReaderTests.cs
git commit -m "feat(dotnet): wide samples reader coercing values to double"
```

---

### Task 4: Replay pacer

**Files:**
- Create: `dotnet/src/TelemetryPoc.Core/Pacer.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/PacerTests.cs`

**Interfaces:**
- Produces: `class Pacer` — `Pacer(double speed)` (clamps `speed <= 0` to `1.0`); `long DueOffsetMs(long sampleTsMs)` = `(long)(sampleTsMs / speed)`; `long WaitMs(long sampleTsMs, long elapsedMs)` = `Max(0, DueOffsetMs - elapsedMs)`.

- [ ] **Step 1: Write the failing test**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/PacerTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class PacerTests
{
    [Fact]
    public void Realtime_due_offset_equals_ts()
    {
        var p = new Pacer(1.0);
        Assert.Equal(0, p.DueOffsetMs(0));
        Assert.Equal(100, p.DueOffsetMs(100));
        Assert.Equal(43_200_000, p.DueOffsetMs(43_200_000));
    }

    [Fact]
    public void Fast_forward_compresses_time()
    {
        Assert.Equal(100, new Pacer(10.0).DueOffsetMs(1000));
    }

    [Fact]
    public void Wait_never_negative_and_accounts_for_elapsed()
    {
        var p = new Pacer(1.0);
        Assert.Equal(100, p.WaitMs(100, 0));
        Assert.Equal(60, p.WaitMs(100, 40));
        Assert.Equal(0, p.WaitMs(100, 500));
    }

    [Fact]
    public void Nonpositive_speed_clamps_to_realtime()
    {
        Assert.Equal(100, new Pacer(0).DueOffsetMs(100));
        Assert.Equal(100, new Pacer(-5).DueOffsetMs(100));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter PacerTests`
Expected: compile error — `Pacer` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// dotnet/src/TelemetryPoc.Core/Pacer.cs
namespace TelemetryPoc.Core;

public sealed class Pacer
{
    private readonly double _speed;
    public Pacer(double speed) => _speed = speed <= 0 ? 1.0 : speed;
    public long DueOffsetMs(long sampleTsMs) => (long)(sampleTsMs / _speed);
    public long WaitMs(long sampleTsMs, long elapsedMs) => Math.Max(0, DueOffsetMs(sampleTsMs) - elapsedMs);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `dotnet/`): `dotnet test --filter PacerTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/Pacer.cs dotnet/tests/TelemetryPoc.Core.Tests/PacerTests.cs
git commit -m "feat(dotnet): deterministic replay pacer"
```

---

### Task 5: Value formatting + enum decode

**Files:**
- Create: `dotnet/src/TelemetryPoc.Core/ValueFormat.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/ValueFormatTests.cs`

**Interfaces:**
- Produces:
  - `ValueFormat.BuildEnumIndex(IEnumerable<EnumValue>) -> IReadOnlyDictionary<(long ChannelId, long Code), EnumValue>`
  - `ValueFormat.DecodeEnum(long channelId, double value, IReadOnlyDictionary<(long,long),EnumValue> index) -> EnumValue?` (rounds value)
  - `ValueFormat.FormatValue(ChannelMeta ch, double value, IReadOnlyDictionary<(long,long),EnumValue> index) -> string` — enum→label, real→`F3` invariant, else invariant number
  - `ValueFormat.SeverityColor(string? severity) -> string` — critical→`#d22`, ok→`#2a2`, else `#888`

- [ ] **Step 1: Write the failing test**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/ValueFormatTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class ValueFormatTests
{
    private static readonly IReadOnlyDictionary<(long, long), EnumValue> Idx =
        ValueFormat.BuildEnumIndex(new[]
        {
            new EnumValue(15, 0, "Normal", "ok"),
            new EnumValue(15, 1, "Critical", "critical"),
        });

    private static ChannelMeta Ch(string type, long id = 1) =>
        new(id, "x", "x", "", type, 0, 1, "table", 1, "");

    [Fact]
    public void DecodeEnum_rounds_and_resolves()
    {
        Assert.Equal("Critical", ValueFormat.DecodeEnum(15, 0.7, Idx)?.Label);
        Assert.Null(ValueFormat.DecodeEnum(15, 9, Idx));
    }

    [Fact]
    public void FormatValue_enum_to_label()
        => Assert.Equal("Critical", ValueFormat.FormatValue(Ch("enum", 15), 1, Idx));

    [Fact]
    public void FormatValue_real_to_three_decimals_invariant()
        => Assert.Equal("1.235", ValueFormat.FormatValue(Ch("real"), 1.23456, Idx));

    [Fact]
    public void SeverityColor_maps_known_and_default()
    {
        Assert.Equal("#d22", ValueFormat.SeverityColor("critical"));
        Assert.Equal("#2a2", ValueFormat.SeverityColor("ok"));
        Assert.Equal("#888", ValueFormat.SeverityColor(null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter ValueFormatTests`
Expected: compile error — `ValueFormat` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// dotnet/src/TelemetryPoc.Core/ValueFormat.cs
using System.Globalization;

namespace TelemetryPoc.Core;

public static class ValueFormat
{
    public static IReadOnlyDictionary<(long ChannelId, long Code), EnumValue> BuildEnumIndex(IEnumerable<EnumValue> enums)
    {
        var d = new Dictionary<(long, long), EnumValue>();
        foreach (var e in enums) d[(e.ChannelId, e.Code)] = e;
        return d;
    }

    public static EnumValue? DecodeEnum(long channelId, double value, IReadOnlyDictionary<(long, long), EnumValue> index)
        => index.TryGetValue((channelId, (long)Math.Round(value)), out var e) ? e : null;

    public static string FormatValue(ChannelMeta ch, double value, IReadOnlyDictionary<(long, long), EnumValue> index)
        => ch.Type switch
        {
            "enum" => DecodeEnum(ch.Id, value, index)?.Label ?? Math.Round(value).ToString(CultureInfo.InvariantCulture),
            "real" => value.ToString("F3", CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    public static string SeverityColor(string? severity)
        => severity switch { "critical" => "#d22", "ok" => "#2a2", _ => "#888" };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `dotnet/`): `dotnet test --filter ValueFormatTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/ValueFormat.cs dotnet/tests/TelemetryPoc.Core.Tests/ValueFormatTests.cs
git commit -m "feat(dotnet): value formatting + enum decode + severity color"
```

---

### Task 6: Process metrics sampler

**Files:**
- Create: `dotnet/src/TelemetryPoc.Core/MetricsSampler.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/MetricsSamplerTests.cs`

**Interfaces:**
- Produces:
  - `record Metrics(double CpuPct, double RamMb)`
  - `class MetricsSampler` — `Metrics Sample()` returns this process's CPU% (delta of `TotalProcessorTime` over wall time / processor count; first call primes and returns 0 CPU) and resident RAM (`WorkingSet64 / 1MiB`).

- [ ] **Step 1: Write the failing test**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/MetricsSamplerTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class MetricsSamplerTests
{
    [Fact]
    public void Sample_returns_finite_nonnegative_metrics()
    {
        var s = new MetricsSampler();
        _ = s.Sample();              // prime
        var m = s.Sample();
        Assert.True(m.RamMb > 0.0);
        Assert.True(m.CpuPct >= 0.0);
        Assert.True(double.IsFinite(m.CpuPct));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter MetricsSamplerTests`
Expected: compile error — `MetricsSampler` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// dotnet/src/TelemetryPoc.Core/MetricsSampler.cs
using System.Diagnostics;

namespace TelemetryPoc.Core;

public sealed record Metrics(double CpuPct, double RamMb);

public sealed class MetricsSampler
{
    private readonly Process _p = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTime _lastTime;
    private bool _primed;

    public Metrics Sample()
    {
        _p.Refresh();
        var now = DateTime.UtcNow;
        var cpu = _p.TotalProcessorTime;
        double pct = 0.0;
        if (_primed)
        {
            var dtMs = (now - _lastTime).TotalMilliseconds;
            var dcMs = (cpu - _lastCpu).TotalMilliseconds;
            if (dtMs > 0) pct = dcMs / (dtMs * Environment.ProcessorCount) * 100.0;
        }
        _lastCpu = cpu;
        _lastTime = now;
        _primed = true;
        return new Metrics(Math.Max(0.0, pct), _p.WorkingSet64 / 1_048_576.0);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `dotnet/`): `dotnet test --filter MetricsSamplerTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/MetricsSampler.cs dotnet/tests/TelemetryPoc.Core.Tests/MetricsSamplerTests.cs
git commit -m "feat(dotnet): per-process CPU/RAM metrics sampler"
```

---

### Task 7: Windowed channel series

**Files:**
- Create: `dotnet/src/TelemetryPoc.Core/ChannelSeries.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/ChannelSeriesTests.cs`

**Interfaces:**
- Produces: `class ChannelSeries` — `ChannelSeries(long windowMs)`; `void Push(long tsMs, double value)` (drops samples older than `latestTs - windowMs`); `(IReadOnlyList<long> Xs, IReadOnlyList<double> Ys) Arrays()`; `int Len { get; }`.

- [ ] **Step 1: Write the failing test**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/ChannelSeriesTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class ChannelSeriesTests
{
    [Fact]
    public void Keeps_window_and_evicts_older_samples()
    {
        var s = new ChannelSeries(1000);
        s.Push(0, 10);
        s.Push(500, 11);
        s.Push(1000, 12);
        s.Push(1600, 13); // window [600,1600] -> drops ts 0 and 500
        var (xs, ys) = s.Arrays();
        Assert.Equal(new long[] { 1000, 1600 }, xs);
        Assert.Equal(new double[] { 12, 13 }, ys);
    }

    [Fact]
    public void Arrays_are_parallel_and_len_tracks_count()
    {
        var s = new ChannelSeries(10_000);
        s.Push(0, 1);
        s.Push(100, 2);
        var (xs, ys) = s.Arrays();
        Assert.Equal(xs.Count, ys.Count);
        Assert.Equal(2, s.Len);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter ChannelSeriesTests`
Expected: compile error — `ChannelSeries` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// dotnet/src/TelemetryPoc.Core/ChannelSeries.cs
namespace TelemetryPoc.Core;

public sealed class ChannelSeries
{
    private readonly long _windowMs;
    private readonly List<long> _xs = new();
    private readonly List<double> _ys = new();

    public ChannelSeries(long windowMs) => _windowMs = windowMs;

    public void Push(long tsMs, double value)
    {
        _xs.Add(tsMs);
        _ys.Add(value);
        var cutoff = tsMs - _windowMs;
        int drop = 0;
        while (drop < _xs.Count && _xs[drop] < cutoff) drop++;
        if (drop > 0)
        {
            _xs.RemoveRange(0, drop);
            _ys.RemoveRange(0, drop);
        }
    }

    public (IReadOnlyList<long> Xs, IReadOnlyList<double> Ys) Arrays() => (_xs, _ys);

    public int Len => _xs.Count;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `dotnet/`): `dotnet test --filter ChannelSeriesTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/ChannelSeries.cs dotnet/tests/TelemetryPoc.Core.Tests/ChannelSeriesTests.cs
git commit -m "feat(dotnet): time-windowed channel series buffer"
```

---

### Task 8: Telemetry store (integration)

**Files:**
- Create: `dotnet/src/TelemetryPoc.Core/TelemetryStore.cs`
- Create: `dotnet/tests/TelemetryPoc.Core.Tests/TelemetryStoreTests.cs`

**Interfaces:**
- Produces: `class TelemetryStore` (`TelemetryStore(long windowMs = 60_000)`):
  - `void ApplyMeta(IReadOnlyList<ChannelMeta> channels, IReadOnlyList<EnumValue> enums)` — store channels, build enum index + id→index map, create a `ChannelSeries` per `Widget == "strip"` channel, track `map_lat`/`map_lon` indices, reset latest/gps/metrics/lastEmit.
  - `void ApplyFrame(Sample s, long emitUnixMs)` — ignore if `s.Values.Length != channels.Count`; set latest + `LastEmitUnixMs`; push strip series + gps track.
  - `void ApplyMetrics(Metrics m)`
  - `double? Latest(long channelId)` (O(1) via id→index)
  - `ChannelSeries? Series(long channelId)`
  - `(IReadOnlyList<double> Lat, IReadOnlyList<double> Lon) GpsTrack()`
  - properties: `IReadOnlyList<ChannelMeta> Channels`, `IReadOnlyDictionary<(long,long),EnumValue> EnumIndex`, `long LastEmitUnixMs`, `Metrics? Metrics`

- [ ] **Step 1: Write the failing test**

```csharp
// dotnet/tests/TelemetryPoc.Core.Tests/TelemetryStoreTests.cs
using Xunit;

namespace TelemetryPoc.Core.Tests;

public class TelemetryStoreTests
{
    private static (IReadOnlyList<ChannelMeta>, IReadOnlyList<EnumValue>) Meta()
    {
        var channels = new List<ChannelMeta>
        {
            new(1, "Roll", "roll", "deg", "real", -180, 180, "strip", 1, "I_01"),
            new(2, "Lat", "lat", "deg", "real", 31, 33, "map_lat", 2, "I_09"),
            new(3, "Lon", "lon", "deg", "real", 34, 35, "map_lon", 3, "I_09"),
        };
        var enums = new List<EnumValue> { new(9, 1, "Critical", "critical") };
        return (channels, enums);
    }

    [Fact]
    public void ApplyFrame_records_latest_and_buffers_only_strip_channels()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(100, new double[] { 12.5, 32.0, 34.5 }), 1700000000000);
        Assert.Equal(12.5, s.Latest(1));
        Assert.Equal(1, s.Series(1)!.Len);
        Assert.Null(s.Series(2));
        Assert.Equal(1700000000000, s.LastEmitUnixMs);
    }

    [Fact]
    public void Accumulates_gps_track_from_map_channels()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(0, new double[] { 0, 32.0, 34.5 }), 1);
        s.ApplyFrame(new Sample(100, new double[] { 0, 32.1, 34.6 }), 2);
        var (lat, lon) = s.GpsTrack();
        Assert.Equal(new double[] { 32.0, 32.1 }, lat);
        Assert.Equal(new double[] { 34.5, 34.6 }, lon);
    }

    [Fact]
    public void Ignores_frame_with_mismatched_value_count()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(0, new double[] { 1, 2 }), 1); // too short
        Assert.Null(s.Latest(1));
        Assert.Equal(0, s.Series(1)!.Len);
    }

    [Fact]
    public void Re_meta_resets_latest_and_track()
    {
        var s = new TelemetryStore();
        var (ch, ev) = Meta();
        s.ApplyMeta(ch, ev);
        s.ApplyFrame(new Sample(0, new double[] { 1, 32, 34.5 }), 5);
        s.ApplyMeta(ch, ev);
        Assert.Null(s.Latest(1));
        Assert.Empty(s.GpsTrack().Lat);
        Assert.Equal(0, s.LastEmitUnixMs);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `dotnet/`): `dotnet test --filter TelemetryStoreTests`
Expected: compile error — `TelemetryStore` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// dotnet/src/TelemetryPoc.Core/TelemetryStore.cs
namespace TelemetryPoc.Core;

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
        => _idToIndex.TryGetValue(channelId, out var i) ? _latest[i] : null;

    public ChannelSeries? Series(long channelId)
        => _series.TryGetValue(channelId, out var s) ? s : null;

    public (IReadOnlyList<double> Lat, IReadOnlyList<double> Lon) GpsTrack() => (_lat, _lon);
}
```

- [ ] **Step 4: Run the full suite + verify**

Run (from `dotnet/`): `dotnet test`
Expected: all tests pass (TelemetryDb, SampleReader, Pacer, ValueFormat, MetricsSampler, ChannelSeries, TelemetryStore).

- [ ] **Step 5: Commit**

```bash
git add dotnet/src/TelemetryPoc.Core/TelemetryStore.cs dotnet/tests/TelemetryPoc.Core.Tests/TelemetryStoreTests.cs
git commit -m "feat(dotnet): telemetry store integrating series, enum, gps track"
```

---

## Self-Review

**Spec coverage:**
- DB reader (channels/enum/ride_meta/wide samples, quoted identifiers, double coercion) → Tasks 2,3 ✓
- Deterministic replay pacing → Task 4 ✓
- Value formatting + enum decode + severity color (invariant culture) → Task 5 ✓
- Process CPU/RAM metrics → Task 6 ✓
- Windowed strip series (60s) → Task 7 ✓
- Telemetry store: latest (O(1)), strip series, gps track, re-meta reset, partial-frame guard, latency emit → Task 8 ✓
- net8.0, nullable enabled, reads the Plan-1 DB contract → all ✓

**Placeholder scan:** No TBD/TODO; every step has real C# + commands. ✓

**Type consistency:** `ChannelMeta`/`EnumValue`/`Sample`/`RideMeta` defined in Task 2, consumed in 3,5,7,8. `ChannelSeries` (Task 7) used by `TelemetryStore` (Task 8). `ValueFormat.BuildEnumIndex` (Task 5) used by the store. `Metrics` (Task 6) used by `ApplyMetrics`. `Latest`/`Series`/`GpsTrack` selectors align `Values[i]` to `channels[i]` exactly as `ApplyFrame` fills them. The partial-frame guard, id→index map, and re-meta reset mirror the (review-hardened) Rust store so the two stacks behave identically. ✓

> **Note for the .NET UI plan (later):** the UI hosts a WPF window with a `BlazorWebView`; an in-process replay loop reads `TelemetryDb.LoadSamples`, paces with `Pacer`, and calls `store.ApplyFrame(sample, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())` on a timer; Razor components render via ScottPlot.Blazor (strip charts), SVG/ScottPlot (gauges), Leaflet-via-JS-interop (map), reading `store` selectors. Latency = `now - store.LastEmitUnixMs`. DB path + replay speed should be configurable (env or config).
