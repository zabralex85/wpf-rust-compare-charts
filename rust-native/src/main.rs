use dioxus::prelude::*;
use dioxus_native::{use_wgpu, CustomPaintCtx, CustomPaintSource, DeviceHandle, TextureHandle};

mod data;
mod feed;
mod chart;

fn main() {
    // Launch via the NATIVE renderer (Blitz/Vello) — NOT dioxus-desktop.
    dioxus_native::launch(app);
}

fn app() -> Element {
    // Registers our `LineCanvas` as a custom-paint source; the returned `u64`
    // is the id the renderer uses to find it again on every paint.
    let canvas_id = use_wgpu(LineCanvas::default);

    rsx! {
        div {
            style: "background:#0a0e14;color:#38c5e0;font-family:sans-serif;padding:24px;height:100vh;",
            h1 { "INU-NATIVE" }
            p { "Blitz + Vello — no WebView" }
            // A `<canvas>` is how `dioxus-native` wires an element to a
            // `CustomPaintSource`: `blitz-dom` special-cases the `canvas` tag's
            // `src` attribute, parsing it as the `u64` source id (see
            // blitz-dom's `mutator::load_custom_paint_src`). `src` is not a
            // known dioxus-html attribute for `canvas` (only `width`/`height`
            // are), so it must be written as a quoted/custom attribute name.
            canvas {
                width: 640,
                height: 220,
                "src": "{canvas_id}",
            }
        }
    }
}

/// Renders one static polyline natively via Vello into an off-screen wgpu
/// texture, which `dioxus-native`/`blitz-paint` then composites into the
/// `<canvas>` element's content box.
///
/// # Custom-paint API (real `dioxus-native` 0.7.9 signature — see task-5-report.md)
///
/// There is **no** "here is the window's `Scene`, draw into it" callback.
/// Instead:
///   1. `dioxus_native::use_wgpu(|| MySource) -> u64` registers a
///      `CustomPaintSource` and returns a `source_id`.
///   2. That id is set as `canvas { "src": "{source_id}" }` in RSX.
///   3. Every frame that `<canvas>` is painted, `blitz-paint` calls
///      `CustomPaintSource::render(&mut self, ctx: CustomPaintCtx<'_>, width:
///      u32, height: u32, scale: f64) -> Option<TextureHandle>` on our
///      source. We build our OWN `vello::Scene` + `vello::Renderer`, render it
///      to a `wgpu::Texture` we create, then hand that texture to
///      `ctx.register_texture(texture) -> TextureHandle`, which is what makes
///      it show up in the window's scene as an image brush.
///   4. `resume(&mut self, device_handle: &DeviceHandle)` /
///      `suspend(&mut self)` are lifecycle hooks tied to the window's
///      wgpu device (e.g. on window suspend/resume); this is where we create
///      our own `vello::Renderer` bound to that device.
struct LineCanvas {
    device: Option<DeviceHandle>,
    renderer: Option<vello::Renderer>,
}

impl Default for LineCanvas {
    fn default() -> Self {
        Self {
            device: None,
            renderer: None,
        }
    }
}

impl CustomPaintSource for LineCanvas {
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
        .expect("failed to create off-screen vello::Renderer for custom-paint canvas");
        self.device = Some(device_handle.clone());
        self.renderer = Some(renderer);
    }

    fn suspend(&mut self) {
        self.device = None;
        self.renderer = None;
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

        // Static hardcoded ramp -> screen-space points -> polyline.
        let pts = chart::to_screen(
            &[(0, 0.0), (500, 1.0), (1000, 0.2)],
            1000,
            width as f32,
            height as f32,
            0.0,
            1.0,
        );
        let mut scene = vello::Scene::new();
        chart::paint_line(
            &mut scene,
            &pts,
            vello::peniko::Color::from_rgb8(0x38, 0xc5, 0xe0),
            2.0,
        );

        let texture = device_handle.device.create_texture(&wgpu::TextureDescriptor {
            label: Some("rust-native-chart-line"),
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

        renderer
            .render_to_texture(
                &device_handle.device,
                &device_handle.queue,
                &scene,
                &view,
                &vello::RenderParams {
                    base_color: vello::peniko::Color::TRANSPARENT,
                    width,
                    height,
                    antialiasing_method: vello::AaConfig::Area,
                },
            )
            .ok()?;

        Some(ctx.register_texture(texture))
    }
}
