//! Fourth variant — an immediate-mode Rust dashboard on `egui`/`eframe` (glow/
//! OpenGL backend). No HTML/CSS engine, no Vello, no WebView — the lightweight
//! end of the spectrum. Same ride, same grouped param table + strip charts +
//! perf HUD, reusing `app_lib` (db/replay/metrics) in-process.

use std::collections::HashMap;
use std::time::{Duration, Instant};

use app_lib::db::{load_channels, load_enum_values, load_samples, ChannelMeta, Sample};
use app_lib::metrics::{Metrics, MetricsSampler};
use app_lib::replay::Pacer;
use eframe::egui;
use rusqlite::Connection;

const WINDOW_MS: i64 = 60_000;

/// Fixed param grouping, mirroring the Tauri `groups.ts` / rust-native.
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

fn group_of(col: &str) -> &'static str {
    for (name, cols) in GROUPS {
        if cols.contains(&col) {
            return name;
        }
    }
    "System"
}

struct Strip {
    name: String,
    unit: String,
    min: f64,
    max: f64,
    col: usize,
    points: Vec<(i64, f64)>,
}

impl Strip {
    fn push(&mut self, ts: i64, v: f64) {
        self.points.push((ts, v));
        let cutoff = ts - WINDOW_MS;
        let drop = self.points.iter().take_while(|(t, _)| *t < cutoff).count();
        if drop > 0 {
            self.points.drain(0..drop);
        }
    }
}

struct Dash {
    channels: Vec<ChannelMeta>,
    enum_index: HashMap<(i64, i64), (String, String)>,
    samples: Vec<Sample>,
    pacer: Pacer,
    cursor: usize,
    start: Instant,
    strips: Vec<Strip>,
    latest: Option<Sample>,
    sampler: MetricsSampler,
    metrics: Metrics,
    last_metrics: Instant,
    frames: u64,
    last_tick: Instant,
    fps_ema: f64,
}

impl Dash {
    fn new(db_path: &str, speed: f64) -> rusqlite::Result<Self> {
        let conn = Connection::open(db_path)?;
        let channels = load_channels(&conn)?;
        let enums = load_enum_values(&conn)?;
        let samples = load_samples(&conn, &channels)?;
        let mut enum_index = HashMap::new();
        for e in &enums {
            enum_index.insert((e.channel_id, e.code), (e.label.clone(), e.severity.clone()));
        }
        let strips = channels
            .iter()
            .enumerate()
            .filter(|(_, c)| c.widget == "strip")
            .map(|(col, c)| Strip {
                name: c.name.clone(),
                unit: c.unit.clone(),
                min: c.min,
                max: c.max,
                col,
                points: Vec::new(),
            })
            .collect();
        Ok(Self {
            channels,
            enum_index,
            samples,
            pacer: Pacer::new(speed),
            cursor: 0,
            start: Instant::now(),
            strips,
            latest: None,
            sampler: MetricsSampler::new(),
            metrics: Metrics { cpu_pct: 0.0, ram_mb: 0.0 },
            last_metrics: Instant::now(),
            frames: 0,
            last_tick: Instant::now(),
            fps_ema: 0.0,
        })
    }

    fn pump(&mut self) {
        let elapsed = self.start.elapsed().as_millis() as i64;
        while self.cursor < self.samples.len()
            && self.pacer.due_offset_ms(self.samples[self.cursor].ts_ms) <= elapsed
        {
            let s = &self.samples[self.cursor];
            for strip in &mut self.strips {
                if let Some(v) = s.values.get(strip.col) {
                    strip.push(s.ts_ms, *v);
                }
            }
            self.latest = Some(s.clone());
            self.cursor += 1;
        }
        if self.last_metrics.elapsed() >= Duration::from_millis(500) {
            self.metrics = self.sampler.sample();
            self.last_metrics = Instant::now();
        }
        let now = Instant::now();
        let dt = now.duration_since(self.last_tick).as_secs_f64();
        self.last_tick = now;
        if dt > 0.0 {
            let inst = 1.0 / dt;
            self.fps_ema = if self.fps_ema == 0.0 { inst } else { self.fps_ema * 0.9 + inst * 0.1 };
        }
        self.frames += 1;
    }

    fn fmt_val(&self, col: usize, is_enum: bool, id: i64) -> (String, egui::Color32) {
        let v = match self.latest.as_ref().and_then(|s| s.values.get(col)) {
            Some(v) => *v,
            None => return ("—".into(), egui::Color32::DARK_GRAY),
        };
        if is_enum {
            if let Some((label, sev)) = self.enum_index.get(&(id, v as i64)) {
                let c = if sev == "critical" {
                    egui::Color32::from_rgb(0xe0, 0x56, 0x4e)
                } else {
                    egui::Color32::from_rgb(0x2f, 0xd1, 0x7a)
                };
                return (label.clone(), c);
            }
        }
        (format!("{v:.3}"), egui::Color32::from_rgb(0xd7, 0xe2, 0xea))
    }
}

const CYAN: egui::Color32 = egui::Color32::from_rgb(0x38, 0xc5, 0xe0);
const DIM: egui::Color32 = egui::Color32::from_rgb(0x8f, 0xa3, 0xb3);

impl eframe::App for Dash {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.pump();
        // Repaint at ~30 Hz (data is 10 Hz) instead of egui's free-running 60 Hz
        // continuous mode — otherwise immediate-mode redraw pins a full core.
        ctx.request_repaint_after(Duration::from_millis(33));

        // ---- Param table (grouped) ----
        egui::SidePanel::left("params")
            .exact_width(300.0)
            .show(ctx, |ui| {
                ui.add_space(6.0);
                ui.colored_label(CYAN, "PARAMETERS");
                ui.separator();
                egui::ScrollArea::vertical().show(ui, |ui| {
                    let order = GROUPS.iter().map(|(n, _)| *n).chain(std::iter::once("System"));
                    for gname in order {
                        let rows: Vec<(usize, &ChannelMeta)> = self
                            .channels
                            .iter()
                            .enumerate()
                            .filter(|(_, c)| group_of(&c.column_name) == gname)
                            .collect();
                        if rows.is_empty() {
                            continue;
                        }
                        ui.add_space(4.0);
                        ui.colored_label(DIM, egui::RichText::new(gname).small());
                        for (col, c) in rows {
                            let (text, color) = self.fmt_val(col, c.type_ == "enum", c.id);
                            ui.horizontal(|ui| {
                                ui.label(egui::RichText::new(&c.name).color(DIM).small());
                                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                                    ui.label(egui::RichText::new(&c.unit).color(DIM).small());
                                    ui.colored_label(color, egui::RichText::new(text).small());
                                });
                            });
                        }
                    }
                });
            });

        // ---- HUD ----
        egui::TopBottomPanel::top("hud").show(ctx, |ui| {
            ui.horizontal(|ui| {
                ui.colored_label(CYAN, egui::RichText::new("INU·EGUI").strong());
                ui.label(egui::RichText::new("egui / glow — immediate mode").color(DIM).small());
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    ui.colored_label(CYAN, format!("FPS {:.0}", self.fps_ema));
                    ui.colored_label(CYAN, format!("RAM {:.0} MB", self.metrics.ram_mb));
                    ui.colored_label(CYAN, format!("CPU {:.1}%", self.metrics.cpu_pct));
                });
            });
        });

        // ---- Chart grid ----
        egui::CentralPanel::default().show(ctx, |ui| {
            let avail = ui.available_size();
            let cols = 2;
            let cw = (avail.x - 12.0) / cols as f32;
            let ch = 150.0;
            egui::ScrollArea::vertical().show(ui, |ui| {
                ui.horizontal_wrapped(|ui| {
                    // collect indices first to avoid borrowing self.strips while drawing
                    for i in 0..self.strips.len() {
                        let (name, unit, min, max, pts, last) = {
                            let s = &self.strips[i];
                            (
                                s.name.clone(),
                                s.unit.clone(),
                                s.min,
                                s.max,
                                s.points.clone(),
                                s.points.last().map(|p| p.1),
                            )
                        };
                        let (rect, _) =
                            ui.allocate_exact_size(egui::vec2(cw - 8.0, ch), egui::Sense::hover());
                        let p = ui.painter_at(rect);
                        p.rect_filled(rect, 3.0, egui::Color32::from_rgb(0x0d, 0x14, 0x20));
                        let val = last.map(|v| format!("{v:.3}")).unwrap_or_else(|| "—".into());
                        p.text(
                            rect.left_top() + egui::vec2(6.0, 4.0),
                            egui::Align2::LEFT_TOP,
                            format!("{name}  {val} {unit}"),
                            egui::FontId::proportional(11.0),
                            DIM,
                        );
                        // polyline: newest at right edge, value inverted into rect
                        if pts.len() >= 2 {
                            let newest = pts[pts.len() - 1].0;
                            let span = (max - min).max(1e-9);
                            let plot = egui::Rect::from_min_max(
                                rect.left_top() + egui::vec2(6.0, 22.0),
                                rect.right_bottom() - egui::vec2(6.0, 6.0),
                            );
                            let poly: Vec<egui::Pos2> = pts
                                .iter()
                                .map(|&(ts, v)| {
                                    let age = (newest - ts) as f32;
                                    let x = plot.right() - (age / WINDOW_MS as f32) * plot.width();
                                    let norm = ((v - min) / span).clamp(0.0, 1.0) as f32;
                                    let y = plot.bottom() - norm * plot.height();
                                    egui::pos2(x, y)
                                })
                                .collect();
                            p.add(egui::Shape::line(poly, egui::Stroke::new(1.5, CYAN)));
                        }
                    }
                });
            });
        });
    }
}

fn resolve_db() -> String {
    if let Ok(p) = std::env::var("RIDE_DB") {
        return p;
    }
    for c in ["../data/ride.db", "../data/ride_small.db"] {
        if std::path::Path::new(c).exists() {
            return c.to_string();
        }
    }
    "../data/ride_small.db".to_string()
}

fn main() -> eframe::Result<()> {
    let db = resolve_db();
    let speed = std::env::var("RIDE_SPEED").ok().and_then(|s| s.parse().ok()).unwrap_or(1.0);
    let dash = Dash::new(&db, speed).unwrap_or_else(|e| panic!("open ride db {db}: {e}"));
    let opts = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default().with_inner_size([1200.0, 700.0]),
        ..Default::default()
    };
    eframe::run_native("INU-EGUI", opts, Box::new(|_cc| Ok(Box::new(dash))))
}
