# Playback Transport Controls — Design Spec

**Status:** design (Phase 8 of the INU-MONITOR Rust UI work)

## Goal

Make the bottom transport bar functional: **pause/resume** and **seek** (scrub to a time) the replay. Today the bar is display-only and the replay is a one-way stream. This adds a small control protocol: the frontend sends commands over the existing WebSocket, the Rust replay loop honors them.

## Architecture

The replay loop currently iterates all in-memory samples linearly and only `send()`s. We make the WebSocket **bidirectional**: split it into a write half (frames out) and a read half (commands in). A reader task ingests JSON commands into shared control state; the replay loop checks that state between samples — freezing on pause and jumping the sample index on seek (re-sending `meta` so the store resets).

```
TransportBar (⏸/▶, scrubber) → onPlayPause / onSeek(ts)
   → ws.send({type:"cmd", action})           [frontend: ws/client.ts gains send()]
        │ WebSocket :9001 (now bidirectional)
        ▼
Rust reader task → Arc<Mutex<Control>> { paused, seek_to } + Notify
        ▼
Replay loop: pause → wait on Notify; seek → jump index + re-send meta + rebase the clock
```

## Control protocol (client → server, JSON over the WS)

```json
{ "type": "cmd", "action": "pause" }
{ "type": "cmd", "action": "resume" }
{ "type": "cmd", "action": "seek", "ts_ms": 123456 }
```

(Server → client messages — `meta`/`frame`/`metrics` — are unchanged. A seek re-sends `meta` first so the client clears its buffers.)

## Components

### 1. Rust — control state + bidirectional WS (`server.rs`, new `control.rs`)

- `CommandMessage { action: "pause"|"resume"|"seek", ts_ms: Option<i64> }` (serde), parsed from inbound text frames. Unknown/malformed → ignored.
- `Control { paused: bool, seek_to: Option<i64> }` behind `Arc<Mutex<>>`, plus a `tokio::sync::Notify` to wake a paused loop on resume/seek.
- `handle_client`: `let (mut write, mut read) = ws.split();` (futures_util `StreamExt`/`SinkExt`). Spawn a reader task: `while let Some(msg) = read.next().await { … }` → parse `CommandMessage` → update `Control` + `notify_waiters()`.
- The samples are already loaded into a `Vec` once; keep them indexed by `i`.

### 2. Rust — pausable/seekable pacer (`replay.rs`, `server.rs` loop)

- The loop tracks a wall-clock base `t0` and index `i`. Effective elapsed = `now - t0`. For each sample, sleep until `effective_elapsed >= sample_ts/speed` (existing `Pacer` math).
- **Each iteration, lock Control:**
  - `seek_to.take()` → set `i = lower_bound(samples by ts_ms, target)`, **re-send `MetaMessage`** (client `applyMeta` resets series/GPS/latest), and **rebase the clock**: `t0 = now - (target_ts / speed)` so the next sample at `target` is due immediately and timing continues correctly.
  - `paused == true` → `notify.notified().await` in a loop until unpaused; on wake, **rebase `t0`** forward by the paused duration so no frames are "due" from the freeze (the replay clock stops while paused).
- Pause/seek must be responsive even while the loop is sleeping between samples → use `tokio::select!` over `sleep(..)` vs `notify.notified()` so a command interrupts the sleep.

### 3. Frontend — WS send (`ws/client.ts`)

- `SocketLike` gains `send(msg: string)`. The client handle exposes `send(cmd: object)` (JSON-stringifies). The live-WS path wires it; mock mode's `send` is a no-op.
- A tiny `encodeCmd(action, tsMs?)` helper (pure, tested) builds the JSON.

### 4. Frontend — TransportBar wired (`TransportBar.tsx`, `AppShell.tsx`)

- TransportBar gains props `paused: boolean`, `onPlayPause(): void`, `onSeek(frac: number): void`. The ⏸/▶ button toggles via `onPlayPause`; the scrubber track `onClick`/drag computes a 0..1 fraction → `onSeek(frac)`. (⏮/⏭ optional — skip to start / +N; can be a later add.)
- `AppShell` owns a `paused` state + the ws `send`. `onPlayPause` → toggle + `send(cmd(paused?"resume":"pause"))`. `onSeek(frac)` → `ts = frac * store.durationMs()` → `send(cmd("seek", ts))` (and optimistically not-paused). The scrubber position keeps rendering from `store.lastTsMs()/durationMs()` as today.

### 5. Store

No change — a seek re-sends `meta`, and `applyMeta()` already clears series/GPS/latest/lastTs. The strip charts + flight track rebuild as frames stream from the new position.

### 6. Mock mode

Mock is a static snapshot (no streaming), so controls are **no-ops in mock** (the `send` is a no-op; nothing to pause/seek). The transport wiring is unit-tested (button → handler, scrubber → onSeek with the right fraction) and the backend pause/seek is cargo-tested; full behavior is GUI-verified. Mock e2e/baselines are unaffected.

## Testing

- **Rust** (`cargo test`, extend `ws_integration`): connect a client; receive `meta` + a couple `frame`s; send `{action:"pause"}` → assert frames **stop** within a window; send `{action:"resume"}` → frames resume; send `{action:"seek", ts_ms:T}` → assert a fresh `meta` arrives and the next `frame.ts_ms >= T`. Pure pacer rebase math unit-tested in `replay.rs`.
- **Frontend** (vitest): `encodeCmd` pure; `ws/client` `send()` forwards to the socket (mockable `SocketLike`); `TransportBar` — ⏸ click calls `onPlayPause`, scrubber click at x% calls `onSeek(≈x)`, paused prop flips the icon; `AppShell` wiring (handler → ws.send with the right command) where feasible.
- **Live verify:** pause freezes the clock + charts; resume continues; scrubbing jumps the replay (charts/track rebuild from the new time).

## Conventions / decisions (locked)

- **Bidirectional over the existing WS** (no new socket/port). Commands are `{type:"cmd", action, ts_ms?}`.
- **Seek = jump the in-memory sample index + re-send meta** (store auto-resets); **pause = freeze the replay clock** (rebase `t0` so timing is seamless on resume). No DB range queries (all samples already in memory).
- **Mock controls are no-ops** (static snapshot); behavior is cargo- + live-verified, not e2e-screenshotted.
- The .NET stack already has an in-process `ReplayPlayer`; matching its controls is **out of scope** here (this phase is the Rust transport; keep the stacks' transports idiomatic).

## Risks / notes

- The pacer clock rebase on pause/seek is the subtle part — get the `t0` math right (covered by a unit test) so resume/seek don't dump a burst of "overdue" frames.
- `tokio::select!` over sleep vs notify is needed for responsive pause while mid-sleep between sparse samples.
- Seeking far forward then back streams a lot of frames quickly (the store rebuilds) — acceptable for a PoC; the buffers are windowed.

## Out of scope

- Variable speed control (a `speed` command) — easy to add later via the same protocol.
- ⏮/⏭ step buttons beyond a simple skip-to-start (optional follow-up).
- .NET transport controls.
