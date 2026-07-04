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

    rsx! {
        div {
            style: "display:flex;flex-direction:row;background:#0a0e14;color:#d7e2ea;\
                     font-family:'Segoe UI',sans-serif;width:100vw;height:100vh;overflow:hidden;",

            // --- Param table ---
            div {
                style: "width:260px;flex-shrink:0;overflow-y:auto;padding:12px;\
                         border-right:1px solid #1c2733;",
                h2 {
                    style: "color:#38c5e0;font-size:14px;margin:0 0 8px 0;font-weight:600;",
                    "PARAMETERS"
                }
                for (i , ch) in channels.iter().enumerate() {
                    div {
                        key: "{ch.id}",
                        style: "display:flex;justify-content:space-between;gap:8px;\
                                 font-size:12px;padding:3px 0;border-bottom:1px solid #131b24;",
                        span { style: "color:#8fa3b3;", "{ch.name}" }
                        span { "{format_value(latest_snapshot.as_ref(), i)} {ch.unit}" }
                    }
                }
            }

            // --- Chart grid + HUD ---
            div {
                style: "flex:1;display:flex;flex-direction:column;padding:12px;overflow-y:auto;",
                div {
                    style: "display:flex;gap:20px;margin-bottom:12px;font-size:12px;color:#8fa3b3;",
                    span { "CPU {cpu_str}%" }
                    span { "RAM {ram_str} MB" }
                    span { "FPS {fps_str}" }
                }
                div {
                    style: "display:flex;flex-wrap:wrap;gap:12px;",
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
    let canvas_id = use_wgpu(move || StripCanvas::new(buffers.clone(), channel_id, min, max));

    rsx! {
        div {
            style: "background:#0d1420;border:1px solid #1c2733;border-radius:4px;padding:6px;",
            div {
                style: "font-size:12px;color:#38c5e0;margin-bottom:4px;",
                "{name}: {value} {unit}"
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
    channel_id: i64,
    min: f64,
    max: f64,
    device: Option<DeviceHandle>,
    renderer: Option<vello::Renderer>,
    target: Option<CanvasTarget>,
}

impl StripCanvas {
    fn new(buffers: Buffers, channel_id: i64, min: f64, max: f64) -> Self {
        Self {
            buffers,
            channel_id,
            min,
            max,
            device: None,
            renderer: None,
            target: None,
        }
    }
}

impl CustomPaintSource for StripCanvas {
    fn resume(&mut self, device_handle: &DeviceHandle) {
        let renderer = vello::Renderer::new(
            &device_handle.device,
            vello::RendererOptions {
                use_cpu: false,
                antialiasing_support: vello::AaSupport::area_only(),
                num_init_threads: None,
                pipeline_cache: None,
            },
        )
        .expect("failed to create off-screen vello::Renderer for strip chart canvas");
        self.device = Some(device_handle.clone());
        self.renderer = Some(renderer);
    }

    fn suspend(&mut self) {
        self.device = None;
        self.renderer = None;
        self.target = None;
    }

    fn render(
        &mut self,
        mut ctx: CustomPaintCtx<'_>,
        width: u32,
        height: u32,
        _scale: f64,
    ) -> Option<TextureHandle> {
        let device_handle = self.device.as_ref()?;
        let renderer = self.renderer.as_mut()?;
        if width == 0 || height == 0 {
            return None;
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
