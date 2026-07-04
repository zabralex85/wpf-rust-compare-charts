use std::any::Any;
use std::cell::RefCell;
use std::rc::Rc;

mod chart;
mod data;
mod feed;
mod ui;

fn main() {
    let db_path = resolve_db_path();
    let speed = std::env::var("RIDE_SPEED")
        .ok()
        .and_then(|s| s.parse::<f64>().ok())
        .unwrap_or(1.0);

    // Provide the `Feed` as a root context so `ui::app` (a plain `fn() ->
    // Element`, per `dioxus_native::launch_cfg`'s signature -- it can't
    // capture anything directly) can `use_context::<Rc<RefCell<Feed>>>()` it.
    // The context closure itself must be `Send + Sync` (only captures
    // `String`/`f64`), but `Feed` and the `Rc<RefCell<_>>` it's wrapped in
    // are only built *inside* the closure body, so they never need to be.
    let contexts: Vec<Box<dyn Fn() -> Box<dyn Any> + Send + Sync>> = vec![Box::new(move || {
        let feed = feed::Feed::open(&db_path, speed)
            .unwrap_or_else(|e| panic!("failed to open ride db {db_path:?}: {e}"));
        Box::new(Rc::new(RefCell::new(feed))) as Box<dyn Any>
    })];

    // Launch via the NATIVE renderer (Blitz/Vello) -- NOT dioxus-desktop.
    dioxus_native::launch_cfg(ui::app, contexts, vec![]);
}

/// `RIDE_DB` env var, else `../data/ride.db`, else `../data/ride_small.db`.
fn resolve_db_path() -> String {
    if let Ok(p) = std::env::var("RIDE_DB") {
        return p;
    }
    for candidate in ["../data/ride.db", "../data/ride_small.db"] {
        if std::path::Path::new(candidate).exists() {
            return candidate.to_string();
        }
    }
    "../data/ride_small.db".to_string()
}
