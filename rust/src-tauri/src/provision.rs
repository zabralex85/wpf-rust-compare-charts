//! Tileset provisioning: local-check → URL download → tilemaker conversion.
//!
//! The testable core is [`ensure_mbtiles_with`], which accepts injected closures
//! for `exists`, `download`, and `convert`. The public entry point
//! [`ensure_mbtiles`] wires the real side-effects.

use std::future::Future;
use std::pin::Pin;

/// Boxed future returned by the `convert` injectable — avoids lifetime-parameter
/// complications that arise when an async fn borrows its `&ProvisionCfg` arg.
type ConvertFut<'a> = Pin<Box<dyn Future<Output = anyhow::Result<()>> + 'a>>;

/// Configuration for the tileset provisioning chain.
pub struct ProvisionCfg {
    pub mbtiles_path: String,
    pub mbtiles_url: Option<String>,
    pub pbf_url: Option<String>,
    pub tilemaker_config: Option<String>,
    pub tilemaker_process: Option<String>,
}

/// Decision chain with injected side-effects (fully unit-testable).
///
/// Order:
/// 1. `exists(path)` true → `Some(path)`
/// 2. `mbtiles_url` present && `download(url, path)` Ok → `Some(path)`
/// 3. `convert(cfg)` Ok → `Some(path)`
/// 4. → `None`
///
/// `download` takes owned `String` params (not `&str`) to avoid higher-ranked
/// trait bound lifetime complications with async closures.
/// `convert` returns a `Pin<Box<dyn Future>>` (see [`ConvertFut`]) so it can
/// borrow `cfg` across await points without requiring a lifetime-parameterized
/// generic type.
pub(crate) async fn ensure_mbtiles_with<EF, DF, DFut, CF>(
    exists: EF,
    download: DF,
    convert: CF,
    cfg: &ProvisionCfg,
) -> Option<String>
where
    EF: Fn(&str) -> bool,
    DF: Fn(String, String) -> DFut,
    DFut: Future<Output = anyhow::Result<()>>,
    CF: for<'a> Fn(&'a ProvisionCfg) -> ConvertFut<'a>,
{
    let path = cfg.mbtiles_path.as_str();

    // Step 1 — local file already present
    if exists(path) {
        return Some(path.to_string());
    }

    // Step 2 — download a pre-built MBTiles
    if let Some(url) = &cfg.mbtiles_url {
        eprintln!("[provision] downloading mbtiles from {url}");
        match download(url.clone(), path.to_string()).await {
            Ok(()) => return Some(path.to_string()),
            Err(e) => eprintln!("[provision] download failed: {e}"),
        }
    }

    // Step 3 — build from source via tilemaker
    eprintln!("[provision] attempting tilemaker convert");
    match convert(cfg).await {
        Ok(()) => return Some(path.to_string()),
        Err(e) => eprintln!("[provision] convert unavailable: {e}"),
    }

    eprintln!(
        "[provision] no tileset available. \
         Set RIDE_MBTILES_URL to a prebuilt israel.mbtiles, \
         or install tilemaker + set RIDE_TILEMAKER_CONFIG/PROCESS, \
         or drop {path} in manually."
    );
    None
}

/// Public entry point — wires real IO into the provisioning chain.
pub async fn ensure_mbtiles(cfg: ProvisionCfg) -> Option<String> {
    ensure_mbtiles_with(
        |p| std::path::Path::new(p).exists(),
        download_to,
        |cfg| Box::pin(run_tilemaker_real(cfg)),
        &cfg,
    )
    .await
}

/// Download `url` to `dest` via a streaming GET, writing to `dest.part` first,
/// then atomically renaming on success. Reports progress via `eprintln!` at
/// ~5% intervals to avoid flooding the log on large files.
/// Never panics; propagates all errors via `anyhow::Result`.
async fn download_to(url: String, dest: String) -> anyhow::Result<()> {
    use futures_util::StreamExt;
    use tokio::io::AsyncWriteExt;
    let tmp = format!("{dest}.part");
    let resp = reqwest::get(&url).await?.error_for_status()?;
    let total = resp.content_length().unwrap_or(0);
    let mut file = tokio::fs::File::create(&tmp).await?;
    let mut stream = resp.bytes_stream();
    let mut got: u64 = 0;
    let mut last_logged_pct: i64 = -5; // force first log at 0%
    while let Some(chunk) = stream.next().await {
        let chunk = chunk?;
        file.write_all(&chunk).await?;
        got += chunk.len() as u64;
        if total > 0 {
            let pct = (got as f64 / total as f64 * 100.0) as i64;
            if pct >= last_logged_pct + 5 {
                last_logged_pct = pct;
                eprintln!("[provision] {pct}% ({got}/{total} bytes)");
            }
        }
    }
    // Always log the final done line
    eprintln!("[provision] 100% done — {got} bytes written to {dest}");
    file.flush().await?;
    tokio::fs::rename(&tmp, &dest).await?;
    Ok(())
}

/// Run `tilemaker` to convert a PBF file into the MBTiles at `cfg.mbtiles_path`.
/// Downloads the PBF from `cfg.pbf_url` if it is not already present locally.
/// Returns an error if tilemaker is unavailable, config files are missing, or
/// the command exits non-zero.
async fn run_tilemaker_real(cfg: &ProvisionCfg) -> anyhow::Result<()> {
    let config = cfg
        .tilemaker_config
        .clone()
        .unwrap_or_else(|| "tiles/config.json".into());
    let process = cfg
        .tilemaker_process
        .clone()
        .unwrap_or_else(|| "tiles/process.lua".into());
    if !std::path::Path::new(&config).exists() || !std::path::Path::new(&process).exists() {
        anyhow::bail!("tilemaker config/process not found ({config} / {process})");
    }
    let pbf_url = cfg
        .pbf_url
        .clone()
        .ok_or_else(|| anyhow::anyhow!("no pbf_url configured"))?;

    // Derive the PBF path next to the mbtiles output file
    let mbtiles_parent = std::path::Path::new(&cfg.mbtiles_path)
        .parent()
        .unwrap_or_else(|| std::path::Path::new("."));
    let pbf_path = mbtiles_parent.join("israel-and-palestine-latest.osm.pbf");
    let pbf = pbf_path.to_string_lossy().into_owned();

    if !std::path::Path::new(&pbf).exists() {
        download_to(pbf_url, pbf.clone()).await?;
    }
    let status = tokio::process::Command::new("tilemaker")
        .args([
            "--input",
            &pbf,
            "--output",
            &cfg.mbtiles_path,
            "--config",
            &config,
            "--process",
            &process,
        ])
        .status()
        .await
        .map_err(|e| anyhow::anyhow!("tilemaker not runnable: {e}"))?;
    if !status.success() {
        anyhow::bail!("tilemaker exited with {status}");
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{Arc, Mutex};

    fn cfg(url: Option<&str>) -> ProvisionCfg {
        ProvisionCfg {
            mbtiles_path: "X/israel.mbtiles".into(),
            mbtiles_url: url.map(String::from),
            pbf_url: None,
            tilemaker_config: None,
            tilemaker_process: None,
        }
    }

    #[tokio::test]
    async fn local_file_wins_no_download_or_convert() {
        let calls = Arc::new(Mutex::new(Vec::<&'static str>::new()));
        let c = calls.clone();
        let out = ensure_mbtiles_with(
            |_p| true, // exists
            move |_u, _d| {
                c.lock().unwrap().push("dl");
                async { Ok(()) }
            },
            |_cfg| Box::pin(async { Ok(()) }),
            &cfg(Some("http://x")),
        )
        .await;
        assert_eq!(out.as_deref(), Some("X/israel.mbtiles"));
        assert!(calls.lock().unwrap().is_empty()); // never downloaded/converted
    }

    #[tokio::test]
    async fn downloads_when_missing_and_url_set() {
        let out = ensure_mbtiles_with(
            |_p| false,
            |_u, _d| async { Ok(()) },
            |_cfg| Box::pin(async { Err(anyhow::anyhow!("no tilemaker")) }),
            &cfg(Some("http://x")),
        )
        .await;
        assert_eq!(out.as_deref(), Some("X/israel.mbtiles"));
    }

    #[tokio::test]
    async fn converts_when_no_url() {
        let out = ensure_mbtiles_with(
            |_p| false,
            |_u, _d| async { Err(anyhow::anyhow!("unused")) },
            |_cfg| Box::pin(async { Ok(()) }),
            &cfg(None),
        )
        .await;
        assert_eq!(out.as_deref(), Some("X/israel.mbtiles"));
    }

    #[tokio::test]
    async fn none_when_nothing_available() {
        let out = ensure_mbtiles_with(
            |_p| false,
            |_u, _d| async { Err(anyhow::anyhow!("dl fail")) },
            |_cfg| Box::pin(async { Err(anyhow::anyhow!("no tilemaker")) }),
            &cfg(Some("http://x")),
        )
        .await;
        assert_eq!(out, None);
    }
}
