use dioxus::prelude::*;

mod data;
mod feed;
mod chart;

fn main() {
    // Launch via the NATIVE renderer (Blitz/Vello) — NOT dioxus-desktop.
    dioxus_native::launch(app);
}

fn app() -> Element {
    rsx! {
        div {
            style: "background:#0a0e14;color:#38c5e0;font-family:sans-serif;padding:24px;height:100vh;",
            h1 { "INU-NATIVE" }
            p { "Blitz + Vello — no WebView" }
        }
    }
}
