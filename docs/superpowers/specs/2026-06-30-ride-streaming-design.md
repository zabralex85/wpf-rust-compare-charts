# Stream Ride Samples from SQLite — Design Spec

**Date:** 2026-06-30
**Status:** Approved (user-directed)

## Goal

Both apps currently load the **entire ride** into memory (`List<Sample>` / `Vec<Sample>`)
before replaying. On the committed 10-min ride that is ~2 MB (negligible), but on a
realistic **12 h ride (432 000 samples)** it measured **+80 MB** working set — the single
largest app-controllable chunk of memory (larger than the whole managed heap).

Stream the samples **forward from SQLite** as the replay clock advances, instead of
materialising them all. Keep parity: do it in **both** stacks (.NET and Rust) so the
perf comparison stays fair. Behavior (frame sequence, timings, seek) is unchanged; only
the memory profile improves (~80 MB saved on a 12 h ride in each stack).

## Shared approach (both stacks)

1. **Eager-load only the metadata** — channels, enum values, ride duration. Small and
   needed up front.
2. **GPS bounds via SQL aggregate** — the map needs the whole-ride bbox immediately for
   its initial fit. Compute it without scanning samples in memory:
   `SELECT MIN(<lat_col>), MIN(<lon_col>), MAX(<lat_col>), MAX(<lon_col>) FROM samples`
   (the lat/lon columns are the channels whose `widget` is `map_lat` / `map_lon`).
3. **Forward sample cursor** — keep an open query
   `SELECT ts, <cols> FROM samples WHERE ts > :last ORDER BY ts` (or seek form below) and
   read rows on demand: each replay tick applies every row with `ts <= rideMs`, peeking
   the next row's `ts` to know when to stop. Only the current row + reader state live in
   memory.
4. **Seek** — re-open the query at `WHERE ts >= :target ORDER BY ts` (`ts` is the
   `samples` PRIMARY KEY, so this is an index seek, cheap). The windowed strip buffers
   and GPS track reset on the existing re-meta path.
5. **What stays in memory:** metadata + the 60 s windowed strip buffers + the GPS track
   accumulated so far (~7 MB over a 12 h ride). The full sample set is never materialised.
6. **Parity is exact** — the cursor yields rows in `ts` order and the applier consumes
   every row with `ts <= rideMs` per tick: the identical frame sequence and timing as the
   load-all version. No observable behavior change.

## .NET sub-project (implemented first)

Onion rings, current names. The replay core (`ReplayPlayer`, `TelemetryStore`,
`ChannelSeries`) and the data adapters are already split across Application / Infrastructure.

### Application (ports + use case)
- **`RideData`** drops `IReadOnlyList<Sample> Samples`; keeps `Channels`, `Enums`,
  `DurationMs`, `GpsBounds`.
- New port **`ISampleCursor : IDisposable`**:
  ```csharp
  long? PeekTs { get; }   // ts of the row at the playhead, or null at end
  Sample Read();          // return the playhead row and advance one
  void SeekTo(long rideMs);  // reposition to the first row with ts >= rideMs
  ```
- **`IRideSource`** gains `ISampleCursor OpenSamples()` (metadata still via `LoadAsync`).
- **`ReplayPlayer`** takes an `ISampleCursor` instead of `IReadOnlyList<Sample>`. Its
  `Advance(rideMs, now)` loops `while (cursor.PeekTs is { } t && t <= rideMs) { store.ApplyFrame(cursor.Read(), now); applied++; }`. `SeekTo(rideMs)` delegates to
  `cursor.SeekTo`. `PeekTs`/`Done` come from the cursor. The emit-count return and the
  store interaction stay identical.

### Infrastructure (adapter)
- **`SqliteRideSource`**: `LoadAsync` loads channels/enums/meta + GPS bounds via the
  `MIN/MAX` SQL; it no longer reads sample rows. `OpenSamples()` returns a
  **`SqliteSampleCursor`** that owns its own read-only `SqliteConnection` +
  `DbDataReader` over `SELECT ts, <cols> FROM samples ORDER BY ts`, exposing
  `PeekTs`/`Read`/`SeekTo` (seek re-executes the command with a `ts >= @target` filter).
  The cursor's connection is disposed with the cursor.

### Composition / lifecycle
- `RideSession` (host) already disposes singletons. The cursor is owned by `RideEngine`
  (created per loaded ride); `RideEngine` becomes `IDisposable` to dispose the cursor, and
  `RideSession.Dispose` disposes the engine. Re-meta on seek keeps the same cursor and
  calls `SeekTo`.

### Tests
- `ReplayPlayerTests` — drive a fake `ISampleCursor` (in-memory list behind the
  interface); assert the same emit sequence/counts as today, plus a `SeekTo` test.
- `RideEngineTests` — `RideData` no longer carries samples; supply a fake cursor; assert
  Advance/Seek parity (the existing assertions on RideMs / emit gate stay).
- `SqliteSampleCursorTests` (new) — against `ride_small.db`: rows in ts order, `PeekTs`
  at end is null, `SeekTo` lands on the first `ts >= target`; bounds SQL returns the
  same bbox the old in-memory `TrackBounds` produced.
- Existing `TelemetryDbTests`/`SampleReaderTests` adjust to the meta-only load + cursor.

### Verification
- `dotnet build` 0/0; full suite green.
- Re-measure on the **12 h ride**: expect working set to fall from ~333 MB back toward
  ~253 MB (the ~80 MB sample load gone).

## Rust sub-project (mirrors; separate spec/plan, done after .NET)

- **`db.rs`**: `load_samples(...) -> Vec<Sample>` is replaced by a streaming reader
  (an open prepared statement iterated row-by-row, or a small struct holding the
  `Connection`/`Statement`), plus a `bounds(conn) -> (minLat,minLon,maxLat,maxLon)` using
  the same `MIN/MAX` SQL.
- **`replay.rs`**: consumes the streaming reader instead of the `Vec`; seek re-queries
  `WHERE ts >= target`.
- Tests: the `db.rs` sample tests become cursor tests; `replay.rs` tests and the ws
  integration test keep asserting the same frame sequence.

## Non-goals

- No behavior/timing/UI change in either app; identical frames, transport, map, HUD.
- No change to the data schema, the simulator, the tile pipeline, or the perf-HUD model.
- No new features. No streaming of the GPS track or strip windows (they stay in memory;
  they are small and already bounded/accumulated as today).
- No async streaming/IAsyncEnumerable — the replay tick is synchronous on the UI/replay
  thread; a synchronous cursor matches the current model.

## Risks

- **Replay parity**: the cursor must yield exactly the same rows in the same order as the
  list; covered by the fake-cursor parity tests + a launch check.
- **Open reader lifetime**: a long-lived `DbDataReader` across the whole replay is fine
  for a read-only SQLite connection; the cursor owns and disposes its own connection so it
  doesn't contend with the metadata-load connection.
- **Seek cost**: re-executing the query per seek is an indexed `ts >=` lookup — cheap;
  acceptable for the occasional transport seek.
