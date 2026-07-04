//! v1 live dashboard: param table + strip charts + perf HUD.
//! Out of scope for v1 (see task-6-brief.md): map, gauges, drag-grid, transport.

use std::cell::RefCell;
use std::collections::HashMap;
use std::rc::Rc;
use std::time::{Duration, Instant};

use dioxus::prelude::*;
use dioxus_native::{use_wgpu, CustomPaintCtx, CustomPaintSource, DeviceHandle, TextureHandle};
use vello::peniko::Color;

use app_lib::db::{ChannelMeta, Sample};
use app_lib::metrics::{Metrics, MetricsSampler};

use crate::chart;
use crate::data::WindowBuffer;
use crate::feed::Feed;

/// Strip-chart scroll window (ms) -- matches the Rust/.NET frontends.
const WINDOW_MS: i64 = 60_000;
const CANVAS_W: u32 = 600;
const CANVAS_H: u32 = 150;

fn cyan() -> Color {
    Color::from_rgb8(0x38, 0xc5, 0xe0)
}

/// Live per-channel strip-chart buffers, keyed by channel id. Written by the
/// feed/metrics coroutine in `app()`; read by each channel's `StripCanvas`
/// paint source. Plain `Rc<RefCell<_>>` rather than a `Signal` -- mutating it
/// should *not* itself schedule a re-render: the coroutine already bumps the
/// `latest`/`metrics`/`fps` signals every tick, and that repaint is what
/// drives every `<canvas>`'s `CustomPaintSource::render` (see task-5-report.md:
/// `blitz-paint` repaints a canvas element whenever the window repaints).
type Buffers = Rc<RefCell<HashMap<i64, WindowBuffer>>>;

/// One `vello::Renderer` shared by every strip canvas. A Renderer allocates
/// sizable GPU/compute buffers, so giving each of the 5 charts its own cost
/// ~94 MB per extra chart (measured: 1 chart 533 MB → 5 charts ~910 MB). All
/// canvases paint sequentially in one repaint pass, so a single shared,
/// interior-mutable Renderer serves them all. Created lazily on first render
/// (against the live device) and cleared on suspend.
type SharedRenderer = Rc<RefCell<Option<vello::Renderer>>>;

#[derive(Clone, PartialEq)]
struct StripMeta {
    idx: usize,
    id: i64,
    name: String,
    unit: String,
    min: f64,
    max: f64,
}

pub fn app() -> Element {
    let feed = use_context::<Rc<RefCell<Feed>>>();

    // Ride metadata never changes after `Feed::open` -- snapshot once per
    // hook (the closures below only ever run on first mount).
    let channels: Vec<ChannelMeta> = use_hook(|| feed.borrow().channels().to_vec());
    let strips: Vec<StripMeta> = use_hook(|| {
        let feed_ref = feed.borrow();
        feed_ref
            .strip_indices()
            .iter()
            .map(|&idx| {
                let c = &feed_ref.channels()[idx];
                StripMeta {
                    idx,
                    id: c.id,
                    name: c.name.clone(),
                    unit: c.unit.clone(),
                    min: c.min,
                    max: c.max,
                }
            })
            .collect::<Vec<_>>()
    });

    // Provided via context (not a prop) so `StripChart` can pick it up
    // without `Buffers` needing to implement `PartialEq` for props diffing --
    // it's mutated through interior mutability, deliberately outside that
    // memoization path (see the `Buffers` type doc comment above).
    let buffers: Buffers = use_context_provider({
        let strips = strips.clone();
        move || {
            let mut map = HashMap::new();
            for s in &strips {
                map.insert(s.id, WindowBuffer::new(WINDOW_MS));
            }
            Rc::new(RefCell::new(map))
        }
    });

    // Single vello::Renderer shared by all strip canvases (see SharedRenderer doc).
    use_context_provider(|| -> SharedRenderer { Rc::new(RefCell::new(None)) });

    let mut latest: Signal<Option<Sample>> = use_signal(|| None);
    let mut metrics: Signal<Metrics> = use_signal(|| Metrics { cpu_pct: 0.0, ram_mb: 0.0 });
    let mut fps: Signal<f64> = use_signal(|| 0.0);

    // Feed + metrics coroutine: ticks ~every 16ms, replays samples due by
    // `elapsed_ms` into the shared buffers, samples CPU/RAM every ~500ms, and
    // bumps the signals above so the whole window (param table, HUD, and
    // every chart canvas) repaints.
    use_future({
        let feed = feed.clone();
        let buffers = buffers.clone();
        let strip_ids: Vec<(usize, i64)> = strips.iter().map(|s| (s.idx, s.id)).collect();
        move || {
            let feed = feed.clone();
            let buffers = buffers.clone();
            let strip_ids = strip_ids.clone();
            async move {
                let start = Instant::now();
                let mut sampler = MetricsSampler::new();
                let mut last_metrics_at = Instant::now();
                let mut frames: u64 = 0;
                loop {
                    futures_timer::Delay::new(Duration::from_millis(16)).await;
                    frames += 1;

                    let elapsed_ms = start.elapsed().as_millis() as i64;
                    let due: Vec<Sample> = feed.borrow_mut().due_upto(elapsed_ms).to_vec();

                    if let Some(last) = due.last() {
                        latest.set(Some(last.clone()));
                    }
                    if !due.is_empty() {
                        let mut map = buffers.borrow_mut();
                        for s in &due {
                            for &(idx, id) in &strip_ids {
                                if let (Some(v), Some(buf)) = (s.values.get(idx), map.get_mut(&id))
                                {
                                    buf.push(s.ts_ms, *v);
                                }
                            }
                        }
                    }

                    if last_metrics_at.elapsed() >= Duration::from_millis(500) {
                        metrics.set(sampler.sample());
                        last_metrics_at = Instant::now();
                    }

                    let secs = start.elapsed().as_secs_f64();
                    if secs > 0.0 {
                        fps.set(frames as f64 / secs);
                    }
                }
            }
        }
    });

    let latest_snapshot = latest.read().clone();
    let m = metrics.read().clone();
    let cpu_str = format!("{:.1}", m.cpu_pct);
    let ram_str = format!("{:.0}", m.ram_mb);
    let fps_str = format!("{:.0}", *fps.read());

    // Group params like the Tauri ParamPanel (groups.ts): fixed group order,
    // channels already arrive in display order, unknown columns fall to "System".
    let grouped = group_channels(&channels);
    let total = channels.len();

    rsx! {
        div {
            style: "display:flex;flex-direction:column;background:#0a0e14;color:#d7e2ea;\
                     font-family:Consolas,'IBM Plex Mono',monospace;width:100vw;height:100vh;overflow:hidden;",

            // --- Top bar ---
            div {
                style: "display:flex;align-items:center;gap:14px;height:44px;flex-shrink:0;\
                         padding:0 16px;background:#0b1119;border-bottom:1px solid #1c2733;",
                span { style: "color:#38c5e0;font-weight:700;font-size:15px;letter-spacing:2px;", "INU·NATIVE" }
                span { style: "color:#5f7385;font-size:10px;letter-spacing:1px;", "BLITZ · VELLO — NATIVE RENDER, NO WEBVIEW" }
                div {
                    style: "margin-left:auto;display:flex;gap:10px;",
                    HudPill { label: "CPU", value: "{cpu_str}%" }
                    HudPill { label: "RAM", value: "{ram_str} MB" }
                    HudPill { label: "FPS", value: "{fps_str}" }
                }
            }

            div {
                style: "display:flex;flex-direction:row;flex:1;overflow:hidden;",

                // --- Param table (grouped) ---
                div {
                    style: "width:290px;flex-shrink:0;overflow-y:auto;\
                             border-right:1px solid #1c2733;background:#0b111a;",
                    div {
                        style: "display:flex;justify-content:space-between;align-items:center;\
                                 padding:9px 12px;border-bottom:1px solid #1c2733;",
                        span { style: "color:#38c5e0;font-size:12px;font-weight:700;letter-spacing:1px;", "PARAMETERS" }
                        span { style: "color:#5f7385;font-size:10px;", "ALL {total} CH" }
                    }
                    for grp in grouped.iter() {
                        div {
                            key: "{grp.name}",
                            style: "display:flex;justify-content:space-between;\
                                     padding:5px 12px;background:#0d1621;color:#6f8496;\
                                     font-size:10px;letter-spacing:1px;text-transform:uppercase;",
                            span { "{grp.name}" }
                            span { style: "color:#455567;", "{grp.rows.len()}" }
                        }
                        for row in grp.rows.iter() {
                            div {
                                key: "{row.id}",
                                style: "display:flex;align-items:center;gap:8px;\
                                         padding:3px 12px;border-bottom:1px solid #101821;font-size:12px;",
                                span { style: "width:6px;height:6px;border-radius:50%;background:#2fd17a;flex-shrink:0;", "" }
                                span { style: "color:#8fa3b3;flex:1;", "{row.name}" }
                                span { style: "color:#d7e2ea;text-align:right;", "{format_value(latest_snapshot.as_ref(), row.index)}" }
                                span { style: "color:#5f7385;width:34px;text-align:right;", "{row.unit}" }
                            }
                        }
                    }
                }

                // --- Chart grid ---
                div {
                    style: "flex:1;display:flex;flex-direction:column;padding:14px;overflow-y:auto;",
                    div {
                        style: "display:flex;flex-wrap:wrap;gap:14px;align-content:flex-start;",
                        for s in strips.iter() {
                            StripChart {
                                key: "{s.id}",
                                name: s.name.clone(),
                                value: format_value(latest_snapshot.as_ref(), s.idx),
                                unit: s.unit.clone(),
                                min: s.min,
                                max: s.max,
                                channel_id: s.id,
                            }
                        }
                    }
                }
            }
        }
    }
}

/// Perf-HUD pill: a dim label + a cyan value, boxed.
#[component]
fn HudPill(label: String, value: String) -> Element {
    rsx! {
        div {
            style: "display:flex;gap:6px;align-items:baseline;padding:3px 9px;\
                     background:#0f1a26;border:1px solid #1c2733;border-radius:3px;",
            span { style: "color:#5f7385;font-size:9px;letter-spacing:1px;", "{label}" }
            span { style: "color:#38c5e0;font-size:12px;font-weight:600;", "{value}" }
        }
    }
}

/// A param row projected into a group (carries the original channel index so
/// the latest sample can still be looked up by position).
struct ParamRowV {
    index: usize,
    id: i64,
    name: String,
    unit: String,
}

struct ParamGroup {
    name: &'static str,
    rows: Vec<ParamRowV>,
}

/// Fixed group order + column membership, mirroring the Tauri `groups.ts`.
const GROUPS: &[(&str, &[&str])] = &[
    ("INU Mode", &["inu_mode1", "inu_mode2"]),
    ("Velocity", &["vel_x", "vel_y", "vel_z", "plat_azim", "vclimb"]),
    (
        "Attitude",
        &[
            "roll", "pitch", "heading_t", "heading_m", "sky_pitch", "sky_roll", "sky_azim",
            "sky_heading", "prsnt_head",
        ],
    ),
    ("Acceleration", &["acc_x", "acc_y", "acc_z"]),
    ("Body Rates", &["roll_r", "pitch_r", "yaw_r"]),
    ("Position", &["lat", "lon"]),
];

fn group_of(column_name: &str) -> &'static str {
    for (name, cols) in GROUPS {
        if cols.contains(&column_name) {
            return name;
        }
    }
    "System"
}

/// Bucket channels into the fixed group order (channels already arrive in
/// display order, so per-group order is preserved); unknowns go to "System".
fn group_channels(channels: &[ChannelMeta]) -> Vec<ParamGroup> {
    let order: Vec<&'static str> = GROUPS.iter().map(|(n, _)| *n).chain(["System"]).collect();
    let mut out = Vec::new();
    for name in order {
        let rows: Vec<ParamRowV> = channels
            .iter()
            .enumerate()
            .filter(|(_, ch)| group_of(&ch.column_name) == name)
            .map(|(index, ch)| ParamRowV {
                index,
                id: ch.id,
                name: ch.name.clone(),
                unit: ch.unit.clone(),
            })
            .collect();
        if !rows.is_empty() {
            out.push(ParamGroup { name, rows });
        }
    }
    out
}

fn format_value(sample: Option<&Sample>, idx: usize) -> String {
    match sample.and_then(|s| s.values.get(idx)) {
        Some(v) => format!("{v:.3}"),
        None => "--".to_string(),
    }
}

/// One strip channel: a title bar (name + latest value + unit) and its
/// scrolling chart canvas.
#[component]
fn StripChart(
    name: String,
    value: String,
    unit: String,
    min: f64,
    max: f64,
    channel_id: i64,
) -> Element {
    let buffers = use_context::<Buffers>();
    let shared = use_context::<SharedRenderer>();
    let canvas_id =
        use_wgpu(move || StripCanvas::new(buffers.clone(), shared.clone(), channel_id, min, max));

    rsx! {
        div {
            style: "background:#0d1420;border:1px solid #1c2733;border-radius:4px;\
                     padding:8px;display:flex;flex-direction:column;",
            div {
                style: "display:flex;justify-content:space-between;align-items:baseline;\
                         margin-bottom:6px;",
                span { style: "font-size:11px;color:#8fa3b3;letter-spacing:1px;text-transform:uppercase;", "{name}" }
                span {
                    style: "font-size:13px;color:#38c5e0;font-weight:600;",
                    "{value} "
                    span { style: "font-size:10px;color:#5f7385;", "{unit}" }
                }
            }
            canvas {
                width: CANVAS_W,
                height: CANVAS_H,
                "src": "{canvas_id}",
            }
        }
    }
}

/// The off-screen render target registered with the window's Vello
/// compositor. Recreated (and the old registration explicitly dropped) only
/// when the canvas is resized; otherwise the *same* GPU texture and the
/// *same* `TextureHandle` are reused every frame, just re-painted in place.
///
/// Task 5's spike registered a brand-new texture every single frame and never
/// unregistered the old one -- fine for one static canvas, but `vello::
/// Renderer::register_texture` (`vello-0.6.0/src/lib.rs:556`) inserts a
/// permanent entry into the window renderer's `image_overrides` map keyed by
/// a fresh id each time, so doing that for 5 continuously-updating strip
/// charts would leak one map entry (holding a GPU texture reference) per
/// chart per repaint. This caches the target instead.
struct CanvasTarget {
    view: wgpu::TextureView,
    width: u32,
    height: u32,
    handle: TextureHandle,
}

struct StripCanvas {
    buffers: Buffers,
    shared: SharedRenderer,
    channel_id: i64,
    min: f64,
    max: f64,
    device: Option<DeviceHandle>,
    target: Option<CanvasTarget>,
}

impl StripCanvas {
    fn new(
        buffers: Buffers,
        shared: SharedRenderer,
        channel_id: i64,
        min: f64,
        max: f64,
    ) -> Self {
        Self {
            buffers,
            shared,
            channel_id,
            min,
            max,
            device: None,
            target: None,
        }
    }
}

impl CustomPaintSource for StripCanvas {
    fn resume(&mut self, device_handle: &DeviceHandle) {
        // The vello::Renderer is created lazily (and shared) in `render`; here we
        // only remember the live device to render/allocate against.
        self.device = Some(device_handle.clone());
    }

    fn suspend(&mut self) {
        self.device = None;
        self.target = None;
        // Drop the shared renderer so the next resume rebuilds it against the
        // new device. Idempotent: every canvas suspends together.
        *self.shared.borrow_mut() = None;
    }

    fn render(
        &mut self,
        mut ctx: CustomPaintCtx<'_>,
        width: u32,
        height: u32,
        _scale: f64,
    ) -> Option<TextureHandle> {
        let device_handle = self.device.as_ref()?.clone();
        if width == 0 || height == 0 {
            return None;
        }

        // Lazily create the single shared renderer against the live device.
        if self.shared.borrow().is_none() {
            let renderer = vello::Renderer::new(
                &device_handle.device,
                vello::RendererOptions {
                    use_cpu: false,
                    antialiasing_support: vello::AaSupport::area_only(),
                    num_init_threads: None,
                    pipeline_cache: None,
                },
            )
            .expect("failed to create shared off-screen vello::Renderer");
            *self.shared.borrow_mut() = Some(renderer);
        }

        let pts = {
            let map = self.buffers.borrow();
            map.get(&self.channel_id)
                .map(|b| {
                    chart::to_screen(
                        b.points(),
                        WINDOW_MS,
                        width as f32,
                        height as f32,
                        self.min,
                        self.max,
                    )
                })
                .unwrap_or_default()
        };

        let mut scene = vello::Scene::new();
        chart::paint_line(&mut scene, &pts, cyan(), 2.0);

        let needs_new_target = match &self.target {
            Some(t) => t.width != width || t.height != height,
            None => true,
        };
        if needs_new_target {
            if let Some(old) = self.target.take() {
                ctx.unregister_texture(old.handle);
            }
            let texture = device_handle.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("rust-native-strip-chart"),
                size: wgpu::Extent3d {
                    width,
                    height,
                    depth_or_array_layers: 1,
                },
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: wgpu::TextureFormat::Rgba8Unorm,
                usage: wgpu::TextureUsages::STORAGE_BINDING
                    | wgpu::TextureUsages::COPY_SRC
                    | wgpu::TextureUsages::TEXTURE_BINDING,
                view_formats: &[],
            });
            let view = texture.create_view(&wgpu::TextureViewDescriptor::default());
            let handle = ctx.register_texture(texture);
            self.target = Some(CanvasTarget {
                view,
                width,
                height,
                handle,
            });
        }

        let target = self.target.as_ref().unwrap();
        let mut shared = self.shared.borrow_mut();
        let renderer = shared.as_mut()?;
        renderer
            .render_to_texture(
                &device_handle.device,
                &device_handle.queue,
                &scene,
                &target.view,
                &vello::RenderParams {
                    base_color: vello::peniko::Color::TRANSPARENT,
                    width,
                    height,
                    antialiasing_method: vello::AaConfig::Area,
                },
            )
            .ok()?;

        Some(target.handle.clone())
    }
}
