# UI Phase 8 — Transport Controls (pause/seek) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the transport bar pause/resume + seek the replay, via a small command protocol over the (now bidirectional) WebSocket.

**Architecture:** The Rust replay loop splits the WS into write (frames) + read (commands). A reader task ingests `{type:"cmd",action,ts_ms?}` into `Arc<Mutex<Control>>` + a `Notify`; the loop checks it between samples — freezing on pause (rebasing the clock) and jumping the sample index on seek (re-sending `meta` so the store resets). The frontend gains `ws.send()`, and the TransportBar's buttons/scrubber call back into AppShell which sends the commands.

**Tech Stack:** Rust (tokio split/select, serde, tokio::sync::Notify), React/TS, vitest, cargo.

## Global Constraints

- Rust: no `unwrap()`/`panic!` on the inbound-command path — malformed/unknown commands are ignored. Server→client `meta`/`frame`/`metrics` shapes unchanged. The existing single-client behavior is preserved.
- TS strict, no `any`. The live-WS path wires `send`; mock mode's `send` is a **no-op**.
- Seek re-sends `meta`; the store's `applyMeta()` already resets series/GPS/latest — **no store change**.
- Pause **freezes the replay clock** (rebase `t0`); seek **jumps the in-memory sample index** + rebases the clock so timing stays seamless (no burst of overdue frames). No DB range queries.
- Command JSON: `{ "type": "cmd", "action": "pause" | "resume" | "seek", "ts_ms"?: number }`.

## File Structure

- `rust/src-tauri/src/control.rs` (new) — `CommandMessage` + `Control` state.
- `rust/src-tauri/src/replay.rs` (modify) — pacer clock-rebase helpers (pure, tested).
- `rust/src-tauri/src/server.rs` (modify) — split WS, reader task, pausable/seekable loop.
- `rust/src-tauri/src/lib.rs` (modify) — `mod control;`.
- `rust/src-tauri/tests/ws_integration.rs` (modify) — pause/resume/seek assertions.
- `rust/src/ws/client.ts` (modify) — `send()`; `rust/src/ws/encode.ts` (new) — `encodeCmd`.
- `rust/src/ui/app/TransportBar.tsx` (modify) — wired buttons/scrubber.
- `rust/src/ui/app/AppShell.tsx` (modify) — paused state + send wiring.
- `rust/src/ui/useTelemetry.ts` (modify) — expose `send` (live) / no-op (mock).
- Test files alongside.

---

### Task 1: Command message + control state (Rust)

**Files:**
- Create: `rust/src-tauri/src/control.rs`; Modify: `rust/src-tauri/src/lib.rs` (`mod control;`)
- Test: `#[cfg(test)]` in `control.rs`

**Interfaces:**
- `#[derive(serde::Deserialize)] pub struct CommandMessage { pub action: String, #[serde(default)] pub ts_ms: Option<i64> }`
- `pub fn parse_command(text: &str) -> Option<CommandMessage>` — returns `None` for non-`cmd`-type or malformed JSON.
- `#[derive(Default)] pub struct Control { pub paused: bool, pub seek_to: Option<i64> }`
- `Control::apply(&mut self, cmd: &CommandMessage)` — `"pause"`→paused=true; `"resume"`→paused=false; `"seek"`→seek_to=cmd.ts_ms (and paused=false). Unknown action → no change.

- [ ] **Step 1: Write the failing test**

```rust
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn parses_and_applies() {
        assert!(parse_command("not json").is_none());
        assert!(parse_command(r#"{"type":"frame"}"#).is_none()); // not a cmd
        let mut c = Control::default();
        c.apply(&parse_command(r#"{"type":"cmd","action":"pause"}"#).unwrap());
        assert!(c.paused);
        c.apply(&parse_command(r#"{"type":"cmd","action":"resume"}"#).unwrap());
        assert!(!c.paused);
        c.apply(&parse_command(r#"{"type":"cmd","action":"seek","ts_ms":4200}"#).unwrap());
        assert_eq!(c.seek_to, Some(4200));
        assert!(!c.paused);
        // unknown action ignored
        let before = (c.paused, c.seek_to);
        c.apply(&parse_command(r#"{"type":"cmd","action":"wat"}"#).unwrap());
        assert_eq!((c.paused, c.seek_to), before);
    }
}
```

- [ ] **Step 2: Run → fail** — `cd rust/src-tauri && cargo test control::tests`

- [ ] **Step 3: Implement**

```rust
#[derive(serde::Deserialize)]
struct Tagged { #[serde(rename = "type")] kind: String }

#[derive(serde::Deserialize)]
pub struct CommandMessage {
    pub action: String,
    #[serde(default)]
    pub ts_ms: Option<i64>,
}

pub fn parse_command(text: &str) -> Option<CommandMessage> {
    let tag: Tagged = serde_json::from_str(text).ok()?;
    if tag.kind != "cmd" { return None; }
    serde_json::from_str(text).ok()
}

#[derive(Default)]
pub struct Control {
    pub paused: bool,
    pub seek_to: Option<i64>,
}

impl Control {
    pub fn apply(&mut self, cmd: &CommandMessage) {
        match cmd.action.as_str() {
            "pause" => self.paused = true,
            "resume" => self.paused = false,
            "seek" => { self.seek_to = cmd.ts_ms; self.paused = false; }
            _ => {}
        }
    }
}
```
Add `pub mod control;` to `lib.rs`.

- [ ] **Step 4: Run → pass**; **Step 5: Commit** `feat(rust): replay command parsing + control state`

---

### Task 2: Pacer clock-rebase math (Rust)

**Files:** Modify `rust/src-tauri/src/replay.rs`; Test: `#[cfg(test)]` there.

**Interfaces (pure, on the existing `Pacer`):**
- `Pacer::rebase_for_seek(&self, target_ts_ms: i64) -> i64` — returns the new `t0` offset such that a sample at `target_ts_ms` is due *now*: `now_ms - target_ts_ms/speed`. (Caller passes `now_ms`; signature: `rebase_for_seek(&self, now_ms: i64, target_ts_ms: i64) -> i64`.)
- `Pacer::rebase_for_pause(&self, t0: i64, paused_ms: i64) -> i64` — `t0 + paused_ms` (shift the base forward by the paused duration).

- [ ] **Step 1: Failing test**

```rust
#[test]
fn rebase_math() {
    let p = Pacer::new(2.0); // speed 2 → due_offset = ts/2
    // seek to ts 1000 at now=5000 → t0 so that 1000 is due now: 5000 - 1000/2 = 4500
    assert_eq!(p.rebase_for_seek(5000, 1000), 4500);
    // pause for 800ms → t0 shifts +800
    assert_eq!(p.rebase_for_pause(4500, 800), 5300);
}
```

- [ ] **Step 2-4:** run (fail) → implement (`(now_ms as f64 - target as f64 / self.speed).round() as i64`; `t0 + paused_ms`) → run (pass) + the existing pacer tests stay green.

- [ ] **Step 5: Commit** `feat(rust): pacer clock-rebase helpers for pause/seek`

---

### Task 3: Bidirectional WS + pausable/seekable replay loop (Rust)

**Files:** Modify `rust/src-tauri/src/server.rs`; Test: extend `rust/src-tauri/tests/ws_integration.rs`.

**Interfaces:** `handle_client` (the per-connection function) is rewritten to: split the WS (`ws.split()` → write/read, `futures_util::{SinkExt, StreamExt}`); hold samples in a `Vec` indexed by `i`; share `Arc<Mutex<control::Control>>` + `Arc<tokio::sync::Notify>`; spawn a reader task parsing inbound text → `control.lock().apply(..)` + `notify.notify_waiters()`. The loop:
- compute `t0` base (wall-clock ms); track `paused_at: Option<i64>`.
- each iteration: `{ let mut c = control.lock(); if let Some(t)=c.seek_to.take() { i = lower_bound(&samples, t); resend meta; t0 = pacer.rebase_for_seek(now_ms(), t); } let paused = c.paused; }` then if `paused` → `notify.notified().await` (looping until unpaused), and on wake `t0 = pacer.rebase_for_pause(t0, paused_duration)`.
- wait for the current sample to be due with `tokio::select!`: a `sleep(due)` branch vs `notify.notified()` branch (so a command interrupts the sleep). Then `write.send(frame)`, `i += 1`.
- `lower_bound(samples, ts)` = first index with `samples[i].ts_ms >= ts` (saturating to len).

> Lock must be a `std::sync::Mutex` (held only briefly, never across `.await`), or `tokio::sync::Mutex`. Do NOT hold the lock across the select/await — copy out `paused`/`seek_to` then drop the guard. The reader task uses the same lock briefly.

- [ ] **Step 1: Extend `ws_integration.rs`** — after receiving `meta` + ≥1 `frame`: send `{"type":"cmd","action":"pause"}`; assert NO `frame` arrives within ~300ms (a `tokio::time::timeout` that should elapse). Send `{"type":"cmd","action":"resume"}`; assert a `frame` arrives. Send `{"type":"cmd","action":"seek","ts_ms":<near end>}`; assert a fresh `meta` arrives then a `frame` with `ts_ms >= target`. (Use `ride_small.db`; pick a seek target within its 10s.)

```rust
// sketch (adapt to the existing test's client helpers)
ws.send(Message::Text(r#"{"type":"cmd","action":"pause"}"#.into())).await.unwrap();
let paused = tokio::time::timeout(Duration::from_millis(300), next_frame(&mut ws)).await;
assert!(paused.is_err(), "frames must stop while paused");
ws.send(Message::Text(r#"{"type":"cmd","action":"resume"}"#.into())).await.unwrap();
assert!(tokio::time::timeout(Duration::from_millis(500), next_frame(&mut ws)).await.is_ok());
```

- [ ] **Step 2: Run → fail** (loop ignores commands) — `cargo test --test ws_integration`
- [ ] **Step 3: Implement** the split/reader/loop per Interfaces.
- [ ] **Step 4: Run → pass**; full `cargo test` green.
- [ ] **Step 5: Commit** `feat(rust): pausable/seekable replay over bidirectional WS`

---

### Task 4: Frontend WS send + command encoder

**Files:** Modify `rust/src/ws/client.ts`; Create `rust/src/ws/encode.ts`; Tests alongside.

**Interfaces:**
- `encode.ts`: `export type CmdAction = "pause" | "resume" | "seek"; export function encodeCmd(action: CmdAction, tsMs?: number): string` → JSON `{"type":"cmd","action",[ts_ms]}`.
- `client.ts`: `SocketLike` gains `send(data: string): void`; the client handle returned gains `send(json: string): void` → `current?.send(json)`. The existing `stop()` + reconnect unchanged.

- [ ] **Step 1: Failing tests** — `encodeCmd("pause")` → `'{"type":"cmd","action":"pause"}'`; `encodeCmd("seek", 4200)` includes `"ts_ms":4200`. `client.send("x")` forwards to a mock `SocketLike.send`.
- [ ] **Step 2-4:** run (fail) → implement → run (pass) + `tsc`.
- [ ] **Step 5: Commit** `feat(rust-ui): WS send() + command encoder`

---

### Task 5: TransportBar wired (buttons + scrubber)

**Files:** Modify `rust/src/ui/app/TransportBar.tsx` + `TransportBar.test.tsx`.

**Interfaces:** new props `paused: boolean; onPlayPause: () => void; onSeek: (frac: number) => void`. The play/pause control shows ▶ when `paused` else ⏸, `onClick={onPlayPause}`. The scrubber track `onClick` computes `frac = clamp((e.clientX - rect.left)/rect.width, 0, 1)` → `onSeek(frac)`. (Keep the existing display props + scrubber position rendering.)

- [ ] **Step 1: Failing tests (jsdom)** — clicking the play/pause control calls `onPlayPause`; `paused={true}` renders ▶ (and `false` → ⏸); clicking the scrubber at a known x calls `onSeek` with the expected fraction (mock `getBoundingClientRect`). Keep existing display tests.
- [ ] **Step 2-4:** run (fail) → implement → run (pass) + `tsc` + `build`.
- [ ] **Step 5: Commit** `feat(rust-ui): wire TransportBar play/pause + seek`

---

### Task 6: AppShell wiring + useTelemetry send

**Files:** Modify `rust/src/ui/useTelemetry.ts`, `rust/src/ui/app/AppShell.tsx`; Test: extend the relevant tests.

**Interfaces:**
- `useTelemetry` return gains `send: (json: string) => void` — the live path forwards to the ws client's `send`; mock path is a no-op (`() => {}`).
- `AppShell`: holds `const [paused, setPaused] = useState(false);` `onPlayPause = () => { setPaused(p => !p); send(encodeCmd(paused ? "resume" : "pause")); }` `onSeek = (frac) => { send(encodeCmd("seek", Math.round(frac * store.durationMs()))); setPaused(false); }`. Pass `paused`/`onPlayPause`/`onSeek` to `<TransportBar>`.

- [ ] **Step 1: Failing test** — render AppShell (or a thin harness) with a mock store + spy `send`; toggling play/pause calls `send` with the pause/resume command; a seek calls `send` with a `seek` command whose `ts_ms ≈ frac*duration`. (If AppShell is awkward to unit-test directly, test the handler logic via a small extracted helper or the TransportBar+spy.)
- [ ] **Step 2-4:** run (fail) → implement → run (pass) + full `npm test` + `tsc` + `build`.
- [ ] **Step 5: Commit** `feat(rust-ui): AppShell sends transport commands`

---

### Task 7: Gate + live verify

- [ ] **Step 1: Full suite** — `cd rust && npx tsc --noEmit && npm test && npm run build && npm run e2e` (baselines unaffected — mock controls are no-ops; the bar still renders) `; cd src-tauri && cargo test`. All green.
- [ ] **Step 2: Live verify** — `RIDE_DB=../../data/ride_small.db RIDE_SPEED=1 npm run tauri dev`: click ⏸ → the clock + strip charts + map track freeze; ▶ → resume; click/drag the scrubber → the replay jumps (charts/track rebuild from the new time, a fresh `meta` resets buffers). 
- [ ] **Step 3:** commit any tweaks; finish the branch (PR).

---

## Self-Review

**Spec coverage:** command parse + state (T1), pacer rebase (T2), bidirectional WS + pausable/seekable loop (T3), frontend send + encoder (T4), wired TransportBar (T5), AppShell/useTelemetry wiring (T6), gate+live (T7). Store unchanged (seek re-sends meta → applyMeta resets). Mock no-op. ✓

**Placeholder scan:** No TBD. The server-loop rewrite (T3) is described by its interface + a sketch rather than a full literal because it threads the existing `handle_client`/DB/`Pacer` code (the implementer reads those); the *testable contracts* — command parse (T1), rebase math (T2), and the integration assertions (T3: pause-stops-frames, resume, seek→meta+ts) — are concrete. ✓

**Type/contract consistency:** `CommandMessage`/`Control` (T1) consumed by the loop (T3); `Pacer::rebase_*` (T2) used in T3; `encodeCmd` (T4) consumed by AppShell (T6); the `{type:"cmd",action,ts_ms?}` JSON is identical across `encodeCmd` (T4) and `parse_command` (T1); `send` threads client.ts (T4) → useTelemetry (T6) → AppShell (T6). ✓
