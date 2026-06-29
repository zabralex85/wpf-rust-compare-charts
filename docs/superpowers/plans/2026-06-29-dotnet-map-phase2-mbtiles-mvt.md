# .NET MVT Skia Map — Phase 2 (MBTiles Reader + MVT Decode) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Read raw vector tiles out of `israel.mbtiles` (SQLite, TMS Y-flip, gunzip) and decode them (Mapbox.VectorTile) into normalized features, plus the pure tile-local→screen pixel transform — bringing the map from "math" to "geometry I can place on screen".

**Architecture:** Two pieces added to `TelemetryPoc.Map`: `MbTilesReader` (a thin SQLite reader — xUnit-tested against a synthetic db) and the MVT layer — `TileProject.ToScreen` (pure, xUnit-tested) + `MapFeature`/`MvtBasemap.DecodeTile` (wraps the `Mapbox.VectorTile` decoder; build-verified since the committed fixture has dummy tiles). No drawing yet (Phase 3).

**Tech Stack:** .NET 8, `Microsoft.Data.Sqlite`, `Mapbox.VectorTile` (mapbox-vector-tile-cs), xUnit.

## Global Constraints

- Adds to `TelemetryPoc.Map` only; `TelemetryPoc.Core` unchanged.
- MBTiles convention: table `tiles(zoom_level, tile_column, tile_row, tile_data BLOB)`; tiles are MVT, usually **gzip**-compressed. TMS→XYZ Y-flip: a request for XYZ `(z,x,y)` reads `tile_row = (1<<z) - 1 - y`.
- Gunzip only when the blob starts with the gzip magic `0x1f 0x8b`; otherwise return the blob unchanged (some MBTiles store uncompressed).
- `MbTilesReader` opens the file **read-only**, returns `null` for a missing tile, never throws on a missing tile.
- Tile-local → screen: a tile covers **256** screen px at its zoom; for a feature point in tile-extent coords `(localX, localY) ∈ [0, extent]`, screen `= (tileScreenX + localX/extent*256, tileScreenY + localY/extent*256)`.

## Existing API (consumed)

- `TileRef(int Z, int X, int Y, double ScreenX, double ScreenY)` (Phase 1) — `ScreenX/Y` is a tile's top-left pixel.
- (Phase 1 `WebMercator`/`TileMath`/`MapStyle`/`Region` unchanged.)

## File Structure

- `dotnet/src/TelemetryPoc.Map/TelemetryPoc.Map.csproj` (modify — add `Microsoft.Data.Sqlite` + `Mapbox.VectorTile`)
- `dotnet/src/TelemetryPoc.Map/MbTilesReader.cs` (new)
- `dotnet/src/TelemetryPoc.Map/TileProject.cs` (new) — `ToScreen`.
- `dotnet/src/TelemetryPoc.Map/MapFeature.cs` (new) — `MvtGeomType`, `MapFeature`.
- `dotnet/src/TelemetryPoc.Map/MvtBasemap.cs` (new) — `DecodeTile`.
- `dotnet/tests/TelemetryPoc.Core.Tests/MbTilesReaderTests.cs`, `TileProjectTests.cs` (new).

---

### Task 1: MbTilesReader (SQLite + Y-flip + gunzip, xUnit)

**Files:**
- Modify: `TelemetryPoc.Map.csproj` (add `Microsoft.Data.Sqlite`)
- Create: `MbTilesReader.cs`, `MbTilesReaderTests.cs`

**Interfaces:**
- Produces:
  - `class MbTilesReader : IDisposable` — `MbTilesReader(string path)`; `byte[]? Read(int z, int x, int y)` (TMS Y-flip + gunzip; `null` if absent).

- [ ] **Step 1: Add the Sqlite package** — `cd dotnet && dotnet add src/TelemetryPoc.Map package Microsoft.Data.Sqlite` (record the version it resolves). The Map csproj gains a `<PackageReference Include="Microsoft.Data.Sqlite" .../>`.

- [ ] **Step 2: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/MbTilesReaderTests.cs`:

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using TelemetryPoc.Map;
using Xunit;

public class MbTilesReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mbtest-{Guid.NewGuid():N}.mbtiles");

    public MbTilesReaderTests()
    {
        using var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        Exec(c, "CREATE TABLE tiles(zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB)");
        // gzip("hello") at z1, col0, TMS row0  → XYZ (1,0,1)
        Insert(c, 1, 0, 0, Gzip(Encoding.UTF8.GetBytes("hello")));
        // uncompressed "raw" at z1, col1, TMS row0 → XYZ (1,1,1)
        Insert(c, 1, 1, 0, Encoding.UTF8.GetBytes("raw"));
    }

    [Fact]
    public void Reads_and_gunzips_with_y_flip()
    {
        using var r = new MbTilesReader(_path);
        var bytes = r.Read(1, 0, 1); // y1 → TMS row (1<<1)-1-1 = 0
        Assert.Equal("hello", Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public void Returns_uncompressed_blob_unchanged()
    {
        using var r = new MbTilesReader(_path);
        Assert.Equal("raw", Encoding.UTF8.GetString(r.Read(1, 1, 1)!));
    }

    [Fact]
    public void Missing_tile_returns_null()
    {
        using var r = new MbTilesReader(_path);
        Assert.Null(r.Read(1, 5, 5));
    }

    private static void Exec(SqliteConnection c, string sql)
    { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private static void Insert(SqliteConnection c, int z, int col, int row, byte[] data)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO tiles VALUES($z,$col,$row,$d)";
        cmd.Parameters.AddWithValue("$z", z);
        cmd.Parameters.AddWithValue("$col", col);
        cmd.Parameters.AddWithValue("$row", row);
        cmd.Parameters.AddWithValue("$d", data);
        cmd.ExecuteNonQuery();
    }

    private static byte[] Gzip(byte[] d)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress)) gz.Write(d, 0, d.Length);
        return ms.ToArray();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // release the file handle before delete
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 3: Run → fail** — (from `dotnet/`) `dotnet test --filter MbTilesReaderTests`

- [ ] **Step 4: Implement `MbTilesReader.cs`**:

```csharp
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace TelemetryPoc.Map;

public sealed class MbTilesReader : IDisposable
{
    private readonly SqliteConnection _conn;

    public MbTilesReader(string path)
    {
        _conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        _conn.Open();
    }

    public byte[]? Read(int z, int x, int y)
    {
        var row = (1 << z) - 1 - y;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT tile_data FROM tiles WHERE zoom_level=$z AND tile_column=$x AND tile_row=$row LIMIT 1";
        cmd.Parameters.AddWithValue("$z", z);
        cmd.Parameters.AddWithValue("$x", x);
        cmd.Parameters.AddWithValue("$row", row);
        if (cmd.ExecuteScalar() is not byte[] blob) return null;
        return Gunzip(blob);
    }

    private static byte[] Gunzip(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
        {
            using var input = new MemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }
        return data;
    }

    public void Dispose() => _conn.Dispose();
}
```

- [ ] **Step 5: Run → pass** — `dotnet test --filter MbTilesReaderTests`, then full `dotnet test`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(dotnet): TelemetryPoc.Map MbTilesReader — SQLite + Y-flip + gunzip (xUnit)"
```

---

### Task 2: Tile-local→screen transform + MVT decode

**Files:**
- Modify: `TelemetryPoc.Map.csproj` (add `Mapbox.VectorTile`)
- Create: `TileProject.cs`, `MapFeature.cs`, `MvtBasemap.cs`, `TileProjectTests.cs`

**Interfaces:**
- Produces:
  - `TileProject.ToScreen(double tileScreenX, double tileScreenY, long localX, long localY, int extent) → (double X, double Y)`
  - `enum MvtGeomType { Point, Line, Polygon, Unknown }`
  - `record MapFeature(string SourceLayer, MvtGeomType Type, IReadOnlyList<IReadOnlyList<(long X, long Y)>> Rings, IReadOnlyDictionary<string,string> Props, int Extent)`
  - `MvtBasemap.DecodeTile(byte[] mvtBytes) → IReadOnlyList<MapFeature>`

- [ ] **Step 1: Add the MVT package** — `cd dotnet && dotnet add src/TelemetryPoc.Map package Mapbox.VectorTile` (record the resolved version). If that exact id fails to resolve, search the NuGet feed for the mapbox-vector-tile-cs C# package and use its id; record what you used.

- [ ] **Step 2: Write the failing test** — `dotnet/tests/TelemetryPoc.Core.Tests/TileProjectTests.cs`:

```csharp
using TelemetryPoc.Map;
using Xunit;

public class TileProjectTests
{
    [Fact]
    public void ToScreen_maps_tile_local_to_pixels()
    {
        // tile top-left at (100,200); a point at half-x, full-y of a 4096 extent
        var (x, y) = TileProject.ToScreen(100, 200, 2048, 4096, 4096);
        Assert.Equal(100 + 0.5 * 256, x, 6); // 228
        Assert.Equal(200 + 1.0 * 256, y, 6); // 456
    }

    [Fact]
    public void ToScreen_origin_is_the_tile_corner()
    {
        var (x, y) = TileProject.ToScreen(50, 60, 0, 0, 4096);
        Assert.Equal(50.0, x, 6);
        Assert.Equal(60.0, y, 6);
    }
}
```

- [ ] **Step 3: Run → fail** — `dotnet test --filter TileProjectTests`

- [ ] **Step 4: Implement `TileProject.cs`**:

```csharp
namespace TelemetryPoc.Map;

public static class TileProject
{
    private const double TileSize = 256.0;

    public static (double X, double Y) ToScreen(double tileScreenX, double tileScreenY, long localX, long localY, int extent)
    {
        var fx = extent == 0 ? 0 : localX / (double)extent;
        var fy = extent == 0 ? 0 : localY / (double)extent;
        return (tileScreenX + fx * TileSize, tileScreenY + fy * TileSize);
    }
}
```

- [ ] **Step 5: Implement `MapFeature.cs`**:

```csharp
namespace TelemetryPoc.Map;

public enum MvtGeomType { Point, Line, Polygon, Unknown }

public sealed record MapFeature(
    string SourceLayer,
    MvtGeomType Type,
    IReadOnlyList<IReadOnlyList<(long X, long Y)>> Rings,
    IReadOnlyDictionary<string, string> Props,
    int Extent);
```

- [ ] **Step 6: Implement `MvtBasemap.cs`** (decode with Mapbox.VectorTile).

> **API ADAPTATION EXPECTED** (like ScottPlot earlier): the `Mapbox.VectorTile` / mapbox-vector-tile-cs surface below — `VectorTileReader(bytes)`, `LayerNames()`, `GetLayer(name)`, `layer.Extent`, `layer.FeatureCount()`, `layer.GetFeature(i)`, `feature.GeometryType`, `feature.Geometry<long>()` (tile-local coords), `feature.GetProperties()`, `Point2d<long>.X/.Y` — is the documented API but member names may differ in the resolved version. If the build errors on an MVT member, inspect the package's public types and adapt; the shape (iterate layers → features → tile-local rings + props) stays the same. Decode is build-verified (the committed fixture has dummy tiles), so confirm it **compiles** and the project builds; full decode is exercised against the real `israel.mbtiles` in Phase 3.

```csharp
using Mapbox.VectorTile;

namespace TelemetryPoc.Map;

public static class MvtBasemap
{
    public static IReadOnlyList<MapFeature> DecodeTile(byte[] mvtBytes)
    {
        var result = new List<MapFeature>();
        var reader = new VectorTileReader(mvtBytes);
        foreach (var layerName in reader.LayerNames())
        {
            var layer = reader.GetLayer(layerName);
            int extent = (int)layer.Extent;
            for (int i = 0; i < layer.FeatureCount(); i++)
            {
                var feat = layer.GetFeature(i);
                var type = feat.GeometryType switch
                {
                    GeomType.POINT => MvtGeomType.Point,
                    GeomType.LINESTRING => MvtGeomType.Line,
                    GeomType.POLYGON => MvtGeomType.Polygon,
                    _ => MvtGeomType.Unknown,
                };
                var rings = new List<IReadOnlyList<(long X, long Y)>>();
                foreach (var ring in feat.Geometry<long>())
                {
                    var pts = new List<(long X, long Y)>(ring.Count);
                    foreach (var p in ring) pts.Add((p.X, p.Y));
                    rings.Add(pts);
                }
                var props = new Dictionary<string, string>();
                foreach (var kv in feat.GetProperties())
                    props[kv.Key] = kv.Value?.ToString() ?? "";
                result.Add(new MapFeature(layerName, type, rings, props, extent));
            }
        }
        return result;
    }
}
```

- [ ] **Step 7: Build + run the pure tests** — (from `dotnet/`) `dotnet build` (0 errors — adapt the MVT API until it compiles) + `dotnet test` (all green; `TileProjectTests` exercise the transform; `MvtBasemap` is build-verified).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(dotnet): TelemetryPoc.Map MVT decode + tile-local→screen transform"
```

---

## Self-Review

**Spec coverage (Phase 2):** `MbTilesReader` SQLite read + Y-flip + gunzip (Task 1); `MvtBasemap.DecodeTile` via Mapbox.VectorTile (Task 2); `TileProject.ToScreen` per-feature tile-local→screen transform (Task 2). The committed fixture has dummy tiles so decode is build-verified; the reader's query/Y-flip/gunzip is tested against a synthetic db. ✓

**Placeholder scan:** No TBD/TODO. The MVT API note is an explicit adapt-on-build instruction (like the proven ScottPlot phase), with full code provided. ✓

**Type consistency:** `MbTilesReader.Read(z,x,y)→byte[]?` feeds `MvtBasemap.DecodeTile(byte[])→IReadOnlyList<MapFeature>`; `MapFeature(SourceLayer,Type,Rings,Props,Extent)` rings are tile-local `(long X,long Y)` consumed by `TileProject.ToScreen(tileScreenX,tileScreenY,localX,localY,extent)`; `TileRef.ScreenX/Y` (Phase 1) is the `tileScreenX/Y` argument. The `MapStyle.SourceLayer` ids (Phase 1) match `MapFeature.SourceLayer` (the MVT layer name) so Phase 3 can join them. Test value `ToScreen(100,200,2048,4096,4096)=(228,456)` verified. ✓

**Note:** `MbTilesReaderTests.Dispose` calls `SqliteConnection.ClearAllPools()` before deleting the temp file so the OS file handle is released (Windows locks the open db file otherwise).
