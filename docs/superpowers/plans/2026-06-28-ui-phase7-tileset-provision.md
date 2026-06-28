# UI Phase 7 — Tileset Auto-Provision Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On startup, ensure `israel.mbtiles` exists — use a local file, else download from a configured URL, else convert via tilemaker — blocking on first run. Drop map labels for now.

**Architecture:** A Rust `provision` module runs a fall-through chain (local → download `RIDE_MBTILES_URL` → tilemaker convert → none) before the tile server starts in the Tauri setup hook. The decision chain is unit-tested via injected closures; the real download/convert are live. The MapLibre style drops the label layer + glyphs (labels deferred).

**Tech Stack:** Rust (reqwest moved to a runtime dep with rustls TLS, tokio, std::process), React/TS (mapStyle tweak), vitest, cargo.

## Global Constraints

- Rust: no `unwrap()`/`panic!` in the provisioning path — every failure logs and falls through; provisioning must NEVER crash app startup (worst case = no mbtiles → tile server 204s).
- `reqwest` becomes a `[dependencies]` entry with `default-features = false, features = ["rustls-tls", "stream"]` (avoid an OpenSSL build dep; `stream` for progress). It stays usable by the existing dev test too.
- TS strict, no `any`. Theme-var rule unaffected (style hex is data).
- No hardcoded default `RIDE_MBTILES_URL` (none is reliably free). `RIDE_PBF_URL` defaults to the geofabrik Israel extract.
- The committed `tiles/fixture.mbtiles` is for Rust tests ONLY — never a runtime fallback for the real map.
- `israel.mbtiles` stays gitignored.

## File Structure

- `rust/src-tauri/src/provision.rs` (new) — provisioning chain + download/convert helpers.
- `rust/src-tauri/src/lib.rs` (modify) — `mod provision;`; blocking provision before the tile server.
- `rust/src-tauri/Cargo.toml` (modify) — `reqwest` → `[dependencies]`.
- `rust/src/ui/app/widgets/mapStyle.ts` (modify) — drop label layer + glyphs.
- `rust/src/ui/app/widgets/mapStyle.test.ts` (modify) — reflect the removal.
- `tiles/README.md` (modify) — provisioning chain + env docs.

---

### Task 1: `provision` module — chain + download/convert

**Files:**
- Create: `rust/src-tauri/src/provision.rs`
- Modify: `rust/src-tauri/src/lib.rs` (`mod provision;`), `rust/src-tauri/Cargo.toml` (`reqwest` to `[dependencies]`)
- Test: `#[cfg(test)]` in `provision.rs`

**Interfaces:**
- `pub struct ProvisionCfg { pub mbtiles_path: String, pub mbtiles_url: Option<String>, pub pbf_url: Option<String>, pub tilemaker_config: Option<String>, pub tilemaker_process: Option<String> }`
- `pub async fn ensure_mbtiles(cfg: ProvisionCfg) -> Option<String>` — wires the real side-effects into the chain.
- `pub(crate) async fn ensure_mbtiles_with<EF, DF, CF>(exists: EF, download: DF, convert: CF, cfg: &ProvisionCfg) -> Option<String>` where the closures abstract the IO so the chain is testable. Order: if `exists(mbtiles_path)` → `Some(path)`; else if `mbtiles_url` set and `download(url, path)` Ok → `Some(path)`; else if `convert(cfg)` Ok → `Some(path)`; else `None`.
- `async fn download_to(url: &str, dest: &str) -> anyhow::Result<()>`, `fn run_tilemaker(cfg: &ProvisionCfg, out: &str) -> anyhow::Result<()>` (the real closures).

- [ ] **Step 1: Write the failing test**

```rust
#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Mutex};

    fn cfg(url: Option<&str>) -> ProvisionCfg {
        ProvisionCfg {
            mbtiles_path: "X/israel.mbtiles".into(),
            mbtiles_url: url.map(String::from),
            pbf_url: None, tilemaker_config: None, tilemaker_process: None,
        }
    }

    #[tokio::test]
    async fn local_file_wins_no_download_or_convert() {
        let calls = Arc::new(Mutex::new(Vec::<&'static str>::new()));
        let c = calls.clone();
        let out = ensure_mbtiles_with(
            |_p| true, // exists
            move |_u, _d| { c.lock().unwrap().push("dl"); async { Ok(()) } },
            |_cfg| { Ok(()) },
            &cfg(Some("http://x")),
        ).await;
        assert_eq!(out.as_deref(), Some("X/israel.mbtiles"));
        assert!(calls.lock().unwrap().is_empty()); // never downloaded/converted
    }

    #[tokio::test]
    async fn downloads_when_missing_and_url_set() {
        let out = ensure_mbtiles_with(
            |_p| false,
            |_u, _d| async { Ok(()) },
            |_cfg| Err(anyhow::anyhow!("no tilemaker")),
            &cfg(Some("http://x")),
        ).await;
        assert_eq!(out.as_deref(), Some("X/israel.mbtiles"));
    }

    #[tokio::test]
    async fn converts_when_no_url() {
        let out = ensure_mbtiles_with(
            |_p| false,
            |_u, _d| async { Err(anyhow::anyhow!("unused")) },
            |_cfg| Ok(()),
            &cfg(None),
        ).await;
        assert_eq!(out.as_deref(), Some("X/israel.mbtiles"));
    }

    #[tokio::test]
    async fn none_when_nothing_available() {
        let out = ensure_mbtiles_with(
            |_p| false,
            |_u, _d| async { Err(anyhow::anyhow!("dl fail")) },
            |_cfg| Err(anyhow::anyhow!("no tilemaker")),
            &cfg(Some("http://x")),
        ).await;
        assert_eq!(out, None);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd rust/src-tauri && cargo test provision::tests`
Expected: FAIL (module missing).

- [ ] **Step 3: Implement `provision.rs`**

```rust
use std::future::Future;
use std::process::Command;

pub struct ProvisionCfg {
    pub mbtiles_path: String,
    pub mbtiles_url: Option<String>,
    pub pbf_url: Option<String>,
    pub tilemaker_config: Option<String>,
    pub tilemaker_process: Option<String>,
}

/// Decision chain with injected side-effects (testable).
pub(crate) async fn ensure_mbtiles_with<EF, DF, DFut, CF>(
    exists: EF,
    download: DF,
    convert: CF,
    cfg: &ProvisionCfg,
) -> Option<String>
where
    EF: Fn(&str) -> bool,
    DF: Fn(&str, &str) -> DFut,
    DFut: Future<Output = anyhow::Result<()>>,
    CF: Fn(&ProvisionCfg) -> anyhow::Result<()>,
{
    let path = cfg.mbtiles_path.as_str();
    if exists(path) {
        return Some(path.to_string());
    }
    if let Some(url) = &cfg.mbtiles_url {
        eprintln!("[provision] downloading mbtiles from {url}");
        match download(url, path).await {
            Ok(()) if exists(path) => return Some(path.to_string()),
            Ok(()) => eprintln!("[provision] download reported ok but file missing"),
            Err(e) => eprintln!("[provision] download failed: {e}"),
        }
    }
    eprintln!("[provision] attempting tilemaker convert");
    match convert(cfg) {
        Ok(()) if exists(path) => return Some(path.to_string()),
        Ok(()) => eprintln!("[provision] convert reported ok but file missing"),
        Err(e) => eprintln!("[provision] convert unavailable: {e}"),
    }
    eprintln!(
        "[provision] no tileset. Set RIDE_MBTILES_URL to a prebuilt israel.mbtiles, \
         or install tilemaker + set RIDE_TILEMAKER_CONFIG/PROCESS, or drop {path} in manually."
    );
    None
}

pub async fn ensure_mbtiles(cfg: ProvisionCfg) -> Option<String> {
    ensure_mbtiles_with(
        |p| std::path::Path::new(p).exists(),
        |u, d| download_to(u.to_string(), d.to_string()),
        run_tilemaker_real,
        &cfg,
    )
    .await
}

async fn download_to(url: String, dest: String) -> anyhow::Result<()> {
    use tokio::io::AsyncWriteExt;
    let tmp = format!("{dest}.part");
    let resp = reqwest::get(&url).await?.error_for_status()?;
    let total = resp.content_length().unwrap_or(0);
    let mut file = tokio::fs::File::create(&tmp).await?;
    let mut stream = resp.bytes_stream();
    let mut got: u64 = 0;
    use futures_util::StreamExt;
    while let Some(chunk) = stream.next().await {
        let chunk = chunk?;
        file.write_all(&chunk).await?;
        got += chunk.len() as u64;
        if total > 0 {
            eprintln!("[provision] {:.0}% ({}/{} bytes)", got as f64 / total as f64 * 100.0, got, total);
        }
    }
    file.flush().await?;
    tokio::fs::rename(&tmp, &dest).await?;
    Ok(())
}

fn run_tilemaker_real(cfg: &ProvisionCfg) -> anyhow::Result<()> {
    let config = cfg.tilemaker_config.clone().unwrap_or_else(|| "tiles/config.json".into());
    let process = cfg.tilemaker_process.clone().unwrap_or_else(|| "tiles/process.lua".into());
    if !std::path::Path::new(&config).exists() || !std::path::Path::new(&process).exists() {
        anyhow::bail!("tilemaker config/process not found ({config} / {process})");
    }
    let pbf_url = cfg.pbf_url.clone().ok_or_else(|| anyhow::anyhow!("no pbf url"))?;
    let pbf = "tiles/israel-and-palestine-latest.osm.pbf";
    if !std::path::Path::new(pbf).exists() {
        // Blocking download of the pbf via a short-lived runtime is acceptable in this fallback path.
        tauri_blocking_download(&pbf_url, pbf)?;
    }
    let status = Command::new("tilemaker")
        .args(["--input", pbf, "--output", &cfg.mbtiles_path, "--config", &config, "--process", &process])
        .status()
        .map_err(|e| anyhow::anyhow!("tilemaker not runnable: {e}"))?;
    if !status.success() {
        anyhow::bail!("tilemaker exited with {status}");
    }
    Ok(())
}

fn tauri_blocking_download(url: &str, dest: &str) -> anyhow::Result<()> {
    let url = url.to_string();
    let dest = dest.to_string();
    tauri::async_runtime::block_on(download_to(url, dest))
}
```

Add `mod provision;` to `lib.rs`. Move `reqwest` to `[dependencies]` (`default-features = false, features = ["rustls-tls", "stream"]`); ensure `futures-util` is available (it already is — used by the WS server).

- [ ] **Step 4: Run to verify it passes**

Run: `cd rust/src-tauri && cargo test provision::tests && cargo build`
Expected: PASS; builds.

- [ ] **Step 5: Commit**

```bash
git add rust/src-tauri/src/provision.rs rust/src-tauri/src/lib.rs rust/src-tauri/Cargo.toml
git commit -m "feat(rust): tileset provisioning chain (local→download→tilemaker)"
```

---

### Task 2: Wire blocking provision into Tauri setup + docs

**Files:**
- Modify: `rust/src-tauri/src/lib.rs`
- Modify: `tiles/README.md`

**Interfaces:** In `setup`, before spawning the tile server, build `ProvisionCfg` from env and `block_on(provision::ensure_mbtiles(cfg))` to get the resolved path; pass it to the tile server (replacing the old `RIDE_MBTILES`/`resolve_default_mbtiles` logic). Env: `RIDE_MBTILES` (explicit override, still honored first), `RIDE_MBTILES_URL`, `RIDE_PBF_URL` (default geofabrik Israel), `RIDE_TILEMAKER_CONFIG`, `RIDE_TILEMAKER_PROCESS`. Default `mbtiles_path` = `RIDE_MBTILES` or `tiles/israel.mbtiles`.

- [ ] **Step 1: Implement** — replace the tile-server env block in `lib.rs`:

```rust
// Resolve/provision the offline tileset (blocking on first run).
let mbtiles = tauri::async_runtime::block_on(crate::provision::ensure_mbtiles(
    crate::provision::ProvisionCfg {
        mbtiles_path: std::env::var("RIDE_MBTILES").unwrap_or_else(|_| "../../tiles/israel.mbtiles".into()),
        mbtiles_url: std::env::var("RIDE_MBTILES_URL").ok(),
        pbf_url: Some(std::env::var("RIDE_PBF_URL").unwrap_or_else(|_|
            "https://download.geofabrik.de/asia/israel-and-palestine-latest.osm.pbf".into())),
        tilemaker_config: std::env::var("RIDE_TILEMAKER_CONFIG").ok(),
        tilemaker_process: std::env::var("RIDE_TILEMAKER_PROCESS").ok(),
    },
));
let glyphs = std::env::var("RIDE_GLYPHS").ok();
tauri::async_runtime::spawn(async move {
    let addr = std::net::SocketAddr::from(([127, 0, 0, 1], tiles_port));
    if let Err(e) = crate::tiles::serve(addr, mbtiles, glyphs).await {
        eprintln!("tile server error: {e}");
    }
});
```

(Remove the now-superseded `resolve_default_mbtiles` helper, or have `ensure_mbtiles` subsume it — the chain's "local exists" step covers the fixture/israel resolution. Keep `tiles_port` as-is.) Note: paths are relative to the dev cwd, consistent with `RIDE_DB`.

- [ ] **Step 2: Build-verify**

Run: `cd rust/src-tauri && cargo build && cargo test`
Expected: builds; tests pass (provision chain + tiles + ws).

- [ ] **Step 3: Update `tiles/README.md`** — add a "Startup provisioning" section:

```markdown
## Startup provisioning (automatic)
On launch the app ensures tiles/israel.mbtiles exists, in order:
1. RIDE_MBTILES (explicit path) or tiles/israel.mbtiles already present → used.
2. RIDE_MBTILES_URL set → downloaded (blocking, console progress) on first run.
3. else tilemaker on PATH + RIDE_TILEMAKER_CONFIG/PROCESS (default tiles/config.json|process.lua)
   → downloads RIDE_PBF_URL (geofabrik Israel) + converts.
4. else → no basemap (SVG map only); console prints instructions.
Labels are skipped for now (no glyphs).
```

- [ ] **Step 4: Commit**

```bash
git add rust/src-tauri/src/lib.rs tiles/README.md
git commit -m "feat(rust): blocking tileset provision in Tauri setup + docs"
```

---

### Task 3: Drop map labels + glyphs from the style

**Files:**
- Modify: `rust/src/ui/app/widgets/mapStyle.ts`, `rust/src/ui/app/widgets/mapStyle.test.ts`

**Interfaces:** `mapStyle(tilesBase)` no longer sets `glyphs` and omits the `place-label` symbol layer (it needed glyphs). Keep `background` + the geometry layers (water/landcover/landuse/transportation casing+line/building). The `sources.basemap` vector source is unchanged.

- [ ] **Step 1: Update the test** — remove the `glyphs` assertion; assert there is **no** layer with `type === "symbol"` (labels gone) and that the geometry layers + `background` remain (`layers.length > 0`, a `water` layer present).

```ts
it("renders geometry only (no label/glyphs)", () => {
  const s = mapStyle("http://127.0.0.1:9002");
  expect(s.sources.basemap).toMatchObject({ type: "vector", url: "http://127.0.0.1:9002/tiles.json" });
  expect("glyphs" in s).toBe(false);
  expect(s.layers.some((l) => l.type === "symbol")).toBe(false);
  expect(s.layers.some((l) => l.id === "water")).toBe(true);
  expect(s.layers.find((l) => l.type === "background")).toBeTruthy();
});
```

- [ ] **Step 2: Run (FAIL) → Step 3: edit `mapStyle.ts`** — delete the `glyphs` property and the `place-label` layer object. → **Step 4: Run (PASS)** + `npx tsc --noEmit` + `npm test` + `npm run build`.

- [ ] **Step 5: Commit**

```bash
git add rust/src/ui/app/widgets/mapStyle.ts rust/src/ui/app/widgets/mapStyle.test.ts
git commit -m "feat(rust-ui): map style geometry-only (labels deferred)"
```

---

### Task 4: Full gate + live verify

- [ ] **Step 1: Full suite**

```bash
cd rust && npx tsc --noEmit && npm test && npm run build && npm run e2e
cd src-tauri && cargo test
```
Expected: all green (provision chain tests included). E2E baselines unaffected (SVG mode unchanged).

- [ ] **Step 2: Live verify** (GUI; needs a real prebuilt mbtiles or tilemaker)

```bash
# with a prebuilt URL:
cd rust && RIDE_DB=../../data/ride_small.db RIDE_MBTILES_URL=<your-israel-mbtiles-url> RIDE_SPEED=2 npm run tauri dev
```
Confirm: first run downloads the mbtiles (console progress), the window opens, toggling the map shows the offline **geometry** basemap (no labels) + the GPS track. Second run starts immediately (file present). With neither URL nor tilemaker, the app still runs (SVG map only) and prints the provisioning instructions.

- [ ] **Step 3: Commit any tweaks**, then finish the branch (PR).

---

## Self-Review

**Spec coverage:** provisioning chain (T1 `ensure_mbtiles_with` + real download/convert), blocking setup wiring + env + docs (T2), labels/glyphs removal (T3), gate+live (T4). Local-first → URL → tilemaker → none, blocking, no-crash fall-through ✓; no hardcoded mbtiles URL, geofabrik pbf default ✓; fixture not a runtime fallback (the chain's `exists` checks the real israel.mbtiles path, not the fixture) ✓. ✓

**Placeholder scan:** No TBD. The real `download_to`/`run_tilemaker` are shown in full; the chain is closure-injected for testability with four ordering tests. tilemaker config/process are env-pathed (README points at tilemaker's bundled OMT resources) rather than committed — a deliberate decision, not a placeholder.

**Type/contract consistency:** `ProvisionCfg` (T1) consumed by T2 wiring; `ensure_mbtiles` returns `Option<String>` consumed as the tile server's `mbtiles` arg (same `Option<String>` the server already accepts from Phase 6); `mapStyle` (T3) drops `glyphs` — the Rust `/glyphs` route remains but is simply unused now. ✓
