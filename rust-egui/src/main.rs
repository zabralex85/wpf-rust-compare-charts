//! Fourth variant — an immediate-mode Rust dashboard on `egui`/`eframe` (glow/
//! OpenGL). No HTML/CSS engine, no Vello, no WebView — the lightweight end.
//! Full INU visual parity (theme, top bar, grouped param table, gauges, strip
//! charts, GPS track, transport bar) EXCEPT the offline MVT basemap. Reuses
//! `app_lib` (db/replay/metrics) in-process.

use std::collections::HashMap;
use std::time::{Duration, Instant};

use app_lib::db::{load_channels, load_enum_values, load_samples, ChannelMeta, Sample};
use app_lib::metrics::{Metrics, MetricsSampler};
use app_lib::tiles::MbTiles;
use eframe::egui;
use egui::{pos2, vec2, Align, Align2, Color32, FontId, Layout, Pos2, Rect, RichText, Stroke};
use rusqlite::Connection;

mod basemap;

const WINDOW_MS: i64 = 60_000;
const GPS_INTERVAL_MS: i64 = 500; // decimate track (matches the other apps' 2 Hz)
const GRID_COLS: i32 = 8; // widget grid width in cells (matches Tauri SEED_COLS)

// ---- INU palette ----
const BG: Color32 = Color32::from_rgb(0x0a, 0x0e, 0x14);
const PANEL: Color32 = Color32::from_rgb(0x0b, 0x11, 0x1a);
const CARD: Color32 = Color32::from_rgb(0x0d, 0x14, 0x20);
const BORDER: Color32 = Color32::from_rgb(0x1c, 0x27, 0x33);
const CYAN: Color32 = Color32::from_rgb(0x38, 0xc5, 0xe0);
const DIM: Color32 = Color32::from_rgb(0x5f, 0x73, 0x85);
const TEXT: Color32 = Color32::from_rgb(0xd7, 0xe2, 0xea);
const GREEN: Color32 = Color32::from_rgb(0x2f, 0xd1, 0x7a);
const RED: Color32 = Color32::from_rgb(0xe0, 0x56, 0x4e);
const AMBER: Color32 = Color32::from_rgb(0xf0, 0xb4, 0x29);

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

fn fmt_clock(ms: i64) -> String {
    let ms = ms.max(0);
    let (h, m, s, f) = (ms / 3_600_000, (ms / 60_000) % 60, (ms / 1000) % 60, ms % 1000);
    format!("{h:02}:{m:02}:{s:02}.{f:03}")
}

#[derive(PartialEq, Clone, Copy)]
enum Kind {
    Line,
    Gauge,
    Map,
}

#[derive(PartialEq, Clone, Copy)]
enum Tab {
    Overview,
    FlightTrack,
    Events,
}

/// Deferred grid interaction, applied after the widget loop (avoids mutating
/// `widgets` while it is borrowed for painting).
#[derive(Clone, Copy)]
enum GridAct {
    Toggle(usize),
    Remove(usize),
    Move(usize, i32, i32),
    Resize(usize, i32, i32),
    Zoom(usize, f64),
    ResetZoom(usize),
}

/// A dashboard widget bound to a channel; can be toggled between a line chart
/// and a radial gauge, or removed. Buffers history so a gauge→line toggle shows
/// the recent trace.
struct Widget {
    name: String,
    unit: String,
    min: f64,
    max: f64,
    col: usize,
    kind: Kind,
    points: Vec<(i64, f64)>,
    gx: i32,   // grid column
    gy: i32,   // grid row
    gw: i32,   // width in cells
    gh: i32,   // height in cells
    zoom: f64, // chart x-zoom (visible window = 60s / zoom)
}

impl Widget {
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
    cursor: usize,
    ride_ms: f64,   // virtual playback position (ms)
    total_ms: i64,  // ride duration
    playing: bool,
    speed: f64,
    widgets: Vec<Widget>,
    lat_col: Option<usize>,
    lon_col: Option<usize>,
    track: Vec<(f64, f64)>,
    last_gps_ts: i64,
    latest: Option<Sample>,
    last_ts: i64,
    sampler: MetricsSampler,
    metrics: Metrics,
    last_metrics: Instant,
    last_tick: Instant,
    fps_ema: f64,
    drag: Option<DragState>,
    tab: Tab,
    mb: Option<MbTiles>,
    view: Option<MapView>,
    tiles: std::collections::HashMap<(u32, u32, u32), Option<basemap::Tile>>,
}

/// Slippy-map view: center lon/lat + fractional zoom.
#[derive(Clone, Copy)]
struct MapView {
    clat: f64,
    clon: f64,
    zoom: f64,
}

/// Active widget drag: move OR resize, decided by where the press landed.
#[derive(Clone, Copy)]
struct DragState {
    idx: usize,
    off: egui::Vec2, // accumulated pointer offset
    resize: bool,    // press landed in the bottom-right grip
    base_gw: i32,
    base_gh: i32,
}

/// One widget entry in the dashboard layout config file.
#[derive(serde::Deserialize)]
struct LayoutItem {
    channel: String,
    kind: String,
    col: i32,
    row: i32,
    cols: i32,
    rows: i32,
}

#[derive(serde::Deserialize)]
struct LayoutCfg {
    widgets: Vec<LayoutItem>,
}

/// Build the widget list from an explicit layout config file (data/dashboard-layout.json
/// by default, or `$RIDE_LAYOUT`). Returns None if the file is missing/invalid or names a
/// channel that isn't in this ride, so the caller falls back to the computed seed.
fn load_layout(channels: &[ChannelMeta]) -> Option<Vec<Widget>> {
    let path = std::env::var("RIDE_LAYOUT").unwrap_or_else(|_| "../data/dashboard-layout.json".into());
    let txt = std::fs::read_to_string(&path).ok()?;
    let cfg: LayoutCfg = serde_json::from_str(&txt).ok()?;
    let mut out = Vec::new();
    for it in &cfg.widgets {
        let kind = match it.kind.as_str() {
            "map" => Kind::Map,
            "gauge" => Kind::Gauge,
            "line" => Kind::Line,
            _ => return None,
        };
        let (col, name, unit, min, max) = if kind == Kind::Map {
            (0usize, "FLIGHT TRACK".to_string(), String::new(), 0.0, 0.0)
        } else {
            let idx = channels.iter().position(|c| c.name == it.channel)?;
            let c = &channels[idx];
            (idx, c.name.clone(), c.unit.clone(), c.min, c.max)
        };
        out.push(Widget {
            name,
            unit,
            min,
            max,
            col,
            kind,
            points: Vec::new(),
            gx: it.col,
            gy: it.row,
            gw: it.cols.max(1),
            gh: it.rows.max(1),
            zoom: 1.0,
        });
    }
    (!out.is_empty()).then_some(out)
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
        let col_of = |pred: &dyn Fn(&ChannelMeta) -> bool| channels.iter().position(pred);
        // Seed gauges first, then line charts (matches the reference default order).
        let mk = |col: usize, c: &ChannelMeta, kind: Kind| Widget {
            name: c.name.clone(),
            unit: c.unit.clone(),
            min: c.min,
            max: c.max,
            col,
            kind,
            points: Vec::new(),
            gx: 0,
            gy: 0,
            gw: if kind == Kind::Gauge { 1 } else { 2 },
            gh: 1,
            zoom: 1.0,
        };
        // Prefer an explicit layout config file (data/dashboard-layout.json, or
        // $RIDE_LAYOUT). Fall back to the computed default seed that mirrors the Tauri
        // seedLayout: map (4×4, when map_lat+map_lon exist) → gauges (1×1, widget=="gauge")
        // → lines (2×1, widget=="strip"), display_order, first-fit packed over 8 cols.
        let widgets = load_layout(&channels).unwrap_or_else(|| {
            let has_lat = channels.iter().any(|c| c.widget == "map_lat");
            let has_lon = channels.iter().any(|c| c.widget == "map_lon");
            let mut widgets: Vec<Widget> = Vec::new();
            if has_lat && has_lon {
                let mut m = mk(0, &channels[0], Kind::Map);
                m.name = "FLIGHT TRACK".into();
                m.unit = String::new();
                m.gw = 4;
                m.gh = 4;
                widgets.push(m);
            }
            widgets.extend(channels.iter().enumerate().filter(|(_, c)| c.widget == "gauge").map(|(col, c)| mk(col, c, Kind::Gauge)));
            widgets.extend(channels.iter().enumerate().filter(|(_, c)| c.widget == "strip").map(|(col, c)| mk(col, c, Kind::Line)));
            // First-fit row-major packer over a GRID_COLS-wide grid (mirrors Tauri seedLayout).
            let mut occ: std::collections::HashSet<(i32, i32)> = std::collections::HashSet::new();
            for w in widgets.iter_mut() {
                'find: for row in 0..1000 {
                    for col in 0..=(GRID_COLS - w.gw) {
                        let free = (col..col + w.gw).all(|c| (row..row + w.gh).all(|r| !occ.contains(&(c, r))));
                        if free {
                            w.gx = col;
                            w.gy = row;
                            for c in col..col + w.gw {
                                for r in row..row + w.gh {
                                    occ.insert((c, r));
                                }
                            }
                            break 'find;
                        }
                    }
                }
            }
            widgets
        });
        let total_ms = samples.last().map(|s| s.ts_ms).unwrap_or(0);

        // Initial map view = centred on the full-ride GPS bbox; open the tileset.
        let mut view = None;
        let mut mb = None;
        if let (Some(la), Some(lo)) = (col_of(&|c| c.widget == "map_lat"), col_of(&|c| c.widget == "map_lon")) {
            let (mut mnla, mut mxla, mut mnlo, mut mxlo) = (f64::MAX, f64::MIN, f64::MAX, f64::MIN);
            let mut any = false;
            for s in &samples {
                if let (Some(&lat), Some(&lon)) = (s.values.get(la), s.values.get(lo)) {
                    mnla = mnla.min(lat);
                    mxla = mxla.max(lat);
                    mnlo = mnlo.min(lon);
                    mxlo = mxlo.max(lon);
                    any = true;
                }
            }
            if any {
                let span = (mxlo - mnlo).abs().max((mxla - mnla).abs()).max(1e-4);
                let zoom = ((360.0 / span * 0.6).log2()).clamp(11.0, 15.0);
                view = Some(MapView { clat: (mnla + mxla) / 2.0, clon: (mnlo + mxlo) / 2.0, zoom });
                let path = std::env::var("RIDE_MBTILES").unwrap_or_else(|_| "../tiles/israel.mbtiles".into());
                mb = MbTiles::open(&path).ok();
            }
        }

        Ok(Self {
            mb,
            view,
            tiles: std::collections::HashMap::new(),
            lat_col: col_of(&|c| c.widget == "map_lat"),
            lon_col: col_of(&|c| c.widget == "map_lon"),
            channels,
            enum_index,
            samples,
            cursor: 0,
            ride_ms: 0.0,
            total_ms,
            playing: true,
            speed,
            widgets,
            track: Vec::new(),
            last_gps_ts: 0,
            latest: None,
            last_ts: 0,
            sampler: MetricsSampler::new(),
            metrics: Metrics { cpu_pct: 0.0, ram_mb: 0.0 },
            last_metrics: Instant::now(),
            last_tick: Instant::now(),
            fps_ema: 0.0,
            drag: None,
            tab: Tab::Overview,
        })
    }

    /// Feed samples with ts <= target into the buffers (forward only).
    fn consume(&mut self, target: i64) {
        while self.cursor < self.samples.len() && self.samples[self.cursor].ts_ms <= target {
            let s = self.samples[self.cursor].clone();
            for w in &mut self.widgets {
                if let Some(v) = s.values.get(w.col) {
                    w.push(s.ts_ms, *v);
                }
            }
            if let (Some(la), Some(lo)) = (self.lat_col, self.lon_col) {
                if self.track.is_empty() || s.ts_ms - self.last_gps_ts >= GPS_INTERVAL_MS {
                    if let (Some(&lat), Some(&lon)) = (s.values.get(la), s.values.get(lo)) {
                        self.track.push((lat, lon));
                        self.last_gps_ts = s.ts_ms;
                    }
                }
            }
            self.last_ts = s.ts_ms;
            self.latest = Some(s);
            self.cursor += 1;
        }
    }

    /// Jump the playhead; seeking backward resets and replays from 0 to target.
    fn seek(&mut self, target_ms: i64) {
        let target = target_ms.clamp(0, self.total_ms);
        if (target as f64) < self.ride_ms {
            self.cursor = 0;
            for w in &mut self.widgets {
                w.points.clear();
            }
            self.track.clear();
            self.last_gps_ts = 0;
            self.latest = None;
            self.last_ts = 0;
        }
        self.ride_ms = target as f64;
        self.consume(target);
    }

    /// Advance one frame: move the virtual clock by real dt × speed (if playing),
    /// feed due samples, sample metrics, update FPS.
    fn tick(&mut self) {
        let now = Instant::now();
        let dt = now.duration_since(self.last_tick).as_secs_f64();
        self.last_tick = now;
        if self.playing {
            self.ride_ms = (self.ride_ms + dt * self.speed * 1000.0).min(self.total_ms as f64);
        }
        let t = self.ride_ms as i64;
        self.consume(t);
        if self.last_metrics.elapsed() >= Duration::from_millis(500) {
            self.metrics = self.sampler.sample();
            self.last_metrics = Instant::now();
        }
        if dt > 0.0 {
            let inst = 1.0 / dt;
            self.fps_ema = if self.fps_ema == 0.0 { inst } else { self.fps_ema * 0.9 + inst * 0.1 };
        }
    }

    fn val(&self, col: usize) -> Option<f64> {
        self.latest.as_ref().and_then(|s| s.values.get(col)).copied()
    }

    fn fmt_param(&self, col: usize, is_enum: bool, id: i64) -> (String, Color32) {
        let v = match self.val(col) {
            Some(v) => v,
            None => return ("—".into(), DIM),
        };
        if is_enum {
            if let Some((label, sev)) = self.enum_index.get(&(id, v as i64)) {
                let c = if sev == "critical" { RED } else { GREEN };
                return (label.clone(), c);
            }
        }
        (format!("{v:.3}"), TEXT)
    }

    /// Add a line widget for `col`, placed on a fresh bottom row, backfilled
    /// with the last 60 s of history so it shows a trace immediately.
    fn add_widget(&mut self, col: usize) {
        if col >= self.channels.len() {
            return;
        }
        let c = &self.channels[col];
        let gy = self.widgets.iter().map(|w| w.gy + w.gh).max().unwrap_or(0);
        let mut w = Widget {
            name: c.name.clone(),
            unit: c.unit.clone(),
            min: c.min,
            max: c.max,
            col,
            kind: Kind::Line,
            points: Vec::new(),
            gx: 0,
            gy,
            gw: 2,
            gh: 1,
            zoom: 1.0,
        };
        let target = self.ride_ms as i64;
        for s in &self.samples {
            if s.ts_ms > target {
                break;
            }
            if s.ts_ms >= target - WINDOW_MS {
                if let Some(v) = s.values.get(col) {
                    w.push(s.ts_ms, *v);
                }
            }
        }
        self.widgets.push(w);
    }

    fn sev_count(&self, sev_match: &str) -> usize {
        self.channels
            .iter()
            .enumerate()
            .filter(|(_, c)| c.type_ == "enum")
            .filter(|(col, c)| {
                self.val(*col)
                    .and_then(|v| self.enum_index.get(&(c.id, v as i64)))
                    .map(|(_, sev)| sev == sev_match)
                    .unwrap_or(false)
            })
            .count()
    }

    fn alarms(&self) -> usize {
        self.sev_count("critical")
    }

    fn cautions(&self) -> usize {
        self.sev_count("warning") + self.sev_count("caution")
    }

    /// Interactive offline slippy map: pan (drag) + zoom (scroll), filled MVT
    /// basemap tiles (cached), road lines, place labels, and the GPS track.
    fn draw_map(&mut self, ui: &mut egui::Ui, rect: Rect, interactive: bool) {
        let p = ui.painter_at(rect);
        p.rect_filled(rect, 3.0, Color32::from_rgb(0x0a, 0x12, 0x1c));
        p.rect_stroke(rect, 3.0, Stroke::new(1.0, BORDER));
        let mut v = match self.view {
            Some(v) => v,
            None => {
                // no GPS -> fallback: track in its own bbox
                draw_track_bbox(&p, rect, &self.track);
                p.text(rect.left_top() + vec2(6.0, 4.0), Align2::LEFT_TOP, "FLIGHT TRACK", FontId::monospace(10.0), DIM);
                return;
            }
        };
        // ---- interaction (pan/zoom only in the full-screen FLIGHT TRACK tab) ----
        let resp = ui.interact(rect, ui.id().with("map"), if interactive { egui::Sense::click_and_drag() } else { egui::Sense::hover() });
        if interactive && resp.dragged() {
            let (cx, cy) = basemap::world(v.clon, v.clat, v.zoom);
            let d = resp.drag_delta();
            let (lon, lat) = basemap::unworld(cx - d.x as f64, cy - d.y as f64, v.zoom);
            v.clon = lon;
            v.clat = lat;
        }
        if interactive && resp.hovered() {
            // consume the wheel so a surrounding ScrollArea doesn't also scroll while zooming
            let scroll = ui.input_mut(|i| {
                let s = i.raw_scroll_delta.y;
                i.raw_scroll_delta = egui::Vec2::ZERO;
                i.smooth_scroll_delta = egui::Vec2::ZERO;
                s
            });
            if scroll != 0.0 {
                v.zoom = (v.zoom + scroll as f64 * 0.004).clamp(10.0, 17.0);
            }
        }
        self.view = Some(v);

        let (cwx, cwy) = basemap::world(v.clon, v.clat, v.zoom);
        let project = |lon: f64, lat: f64| {
            let (wx, wy) = basemap::world(lon, lat, v.zoom);
            pos2(rect.center().x + (wx - cwx) as f32, rect.center().y + (wy - cwy) as f32)
        };
        let unproject = |px: f32, py: f32| basemap::unworld(cwx + (px - rect.center().x) as f64, cwy + (py - rect.center().y) as f64, v.zoom);
        // tileset maxzoom is 14 — cap the fetched zoom there and over-magnify z14 tiles beyond it
        let zi = (v.zoom.floor() as u32).min(14);
        let (lon_a, lat_a) = unproject(rect.left(), rect.top());
        let (lon_b, lat_b) = unproject(rect.right(), rect.bottom());
        let tx0 = basemap::lon_to_tx(lon_a.min(lon_b), zi).floor() as i64;
        let tx1 = basemap::lon_to_tx(lon_a.max(lon_b), zi).floor() as i64;
        let ty0 = basemap::lat_to_ty(lat_a.max(lat_b), zi).floor() as i64;
        let ty1 = basemap::lat_to_ty(lat_a.min(lat_b), zi).floor() as i64;

        let water = Color32::from_rgb(0x14, 0x2f, 0x47);
        let green = Color32::from_rgb(0x13, 0x24, 0x1b);
        let land = Color32::from_rgb(0x0e, 0x17, 0x22);
        let road_col = [
            Color32::from_rgb(0x52, 0x63, 0x74),
            Color32::from_rgb(0x3e, 0x4e, 0x5e),
            Color32::from_rgb(0x2e, 0x3c, 0x4a),
            Color32::from_rgb(0x24, 0x30, 0x3c),
        ];
        let road_w = [1.8, 1.3, 0.9, 0.6];
        let mut labels: Vec<(Pos2, String, u8)> = Vec::new();

        for tx in tx0..=tx1 {
            for ty in ty0..=ty1 {
                if tx < 0 || ty < 0 || (tx1 - tx0 + 1) * (ty1 - ty0 + 1) > 64 {
                    continue;
                }
                let key = (zi, tx as u32, ty as u32);
                if !self.tiles.contains_key(&key) {
                    let decoded = self
                        .mb
                        .as_ref()
                        .and_then(|mb| mb.tile_xyz(zi, tx as u32, ty as u32).ok().flatten())
                        .and_then(|b| basemap::decode(&b));
                    self.tiles.insert(key, decoded);
                }
                let t = match self.tiles.get(&key) {
                    Some(Some(t)) => t,
                    _ => continue,
                };
                let tp = |u: f32, vv: f32| {
                    let (lon, lat) = basemap::tile_to_lonlat(tx as f64 + u as f64, ty as f64 + vv as f64, zi);
                    project(lon, lat)
                };
                for (fill, s) in &t.polys {
                    let pts: Vec<Pos2> = s.iter().map(|&(u, vv)| tp(u, vv)).collect();
                    let col = match fill {
                        basemap::Fill::Water => water,
                        basemap::Fill::Green => green,
                        basemap::Fill::Land => land,
                    };
                    p.add(egui::Shape::convex_polygon(pts, col, Stroke::NONE));
                }
                for (rank, s) in &t.roads {
                    let pts: Vec<Pos2> = s.iter().map(|&(u, vv)| tp(u, vv)).collect();
                    if pts.len() >= 2 {
                        let r = *rank as usize;
                        p.add(egui::Shape::line(pts, Stroke::new(road_w[r], road_col[r])));
                    }
                }
                for (u, vv, txt, rank) in &t.labels {
                    labels.push((tp(*u, *vv), txt.clone(), *rank));
                }
            }
        }

        // GPS track on top
        if self.track.len() >= 2 {
            let pts: Vec<Pos2> = self.track.iter().map(|&(la, lo)| project(lo, la)).collect();
            p.add(egui::Shape::line(pts.clone(), Stroke::new(2.0, CYAN)));
            if let Some(l) = pts.last() {
                p.circle_filled(*l, 4.0, GREEN);
            }
        }
        // declutter: cities (rank 0) win over streets (rank 1); skip labels
        // overlapping already-placed ones (like a real map).
        labels.sort_by_key(|&(_, _, rank)| rank);
        let mut placed: Vec<Pos2> = Vec::new();
        for (pos, txt, rank) in labels {
            if !rect.contains(pos) || placed.iter().any(|q| (q.x - pos.x).abs() < 70.0 && (q.y - pos.y).abs() < 13.0) {
                continue;
            }
            placed.push(pos);
            if placed.len() > 60 {
                break;
            }
            // cities brighter + larger, streets dimmer + smaller
            let (size, col) = if rank == 0 {
                (12.0, Color32::from_rgb(0xdf, 0xea, 0xf2))
            } else {
                (9.5, Color32::from_rgb(0x9f, 0xb2, 0xc0))
            };
            let halo = Color32::from_rgb(0x05, 0x09, 0x0f);
            for d in [vec2(-1.0, 0.0), vec2(1.0, 0.0), vec2(0.0, -1.0), vec2(0.0, 1.0)] {
                p.text(pos + d, Align2::CENTER_CENTER, &txt, FontId::proportional(size), halo);
            }
            p.text(pos, Align2::CENTER_CENTER, &txt, FontId::proportional(size), col);
        }
        p.text(rect.left_top() + vec2(6.0, 4.0), Align2::LEFT_TOP, "FLIGHT TRACK", FontId::monospace(10.0), DIM);
        p.text(rect.right_bottom() + vec2(-6.0, -6.0), Align2::RIGHT_BOTTOM, format!("z{:.1}  drag/scroll", v.zoom), FontId::monospace(8.0), DIM);
    }
}

fn pill(ui: &mut egui::Ui, color: Color32, text: &str) {
    // manual draw at a fixed compact height (a Frame stretches to the font's
    // ascent/descent → tall blocks; the Tauri badge is a short rounded pill).
    let font = FontId::proportional(9.0);
    let tw = ui.fonts(|f| f.layout_no_wrap(text.to_string(), font.clone(), color).size().x);
    let (r, _) = ui.allocate_exact_size(vec2(tw + 14.0, 16.0), egui::Sense::hover());
    let p = ui.painter_at(r);
    p.rect_filled(r, 8.0, color.linear_multiply(0.12));
    p.rect_stroke(r, 8.0, Stroke::new(1.0, color.linear_multiply(0.75)));
    p.text(r.center(), Align2::CENTER_CENTER, text, font, color);
}

/// Radial gauge: 270° arc (opening at the bottom) + a value needle.
fn draw_gauge(p: &egui::Painter, rect: Rect, value: f64, min: f64, max: f64, name: &str, unit: &str) {
    p.rect_filled(rect, 3.0, CARD);
    p.rect_stroke(rect, 3.0, Stroke::new(1.0, BORDER));
    let center = pos2(rect.center().x, rect.top() + rect.height() * 0.52);
    let r = (rect.width().min(rect.height()) * 0.34).max(8.0);
    let start = 135.0_f32.to_radians();
    let sweep = 270.0_f32.to_radians();
    let arc = |a0: f32, a1: f32, col: Color32, w: f32, p: &egui::Painter| {
        let n = 48;
        let pts: Vec<Pos2> = (0..=n)
            .map(|i| {
                let a = a0 + (a1 - a0) * (i as f32 / n as f32);
                center + vec2(a.cos(), a.sin()) * r
            })
            .collect();
        p.add(egui::Shape::line(pts, Stroke::new(w, col)));
    };
    arc(start, start + sweep, BORDER, 3.0, p);
    let t = ((value - min) / (max - min).max(1e-9)).clamp(0.0, 1.0) as f32;
    arc(start, start + sweep * t, CYAN, 3.0, p);
    // needle
    let a = start + sweep * t;
    p.line_segment([center, center + vec2(a.cos(), a.sin()) * (r - 2.0)], Stroke::new(2.0, CYAN));
    p.circle_filled(center, 3.0, CYAN);
    // min/max scale labels at the arc ends
    let end = start + sweep;
    p.text(center + vec2(start.cos(), start.sin()) * (r + 10.0), Align2::CENTER_CENTER, format!("{min:.0}"), FontId::monospace(8.0), DIM);
    p.text(center + vec2(end.cos(), end.sin()) * (r + 10.0), Align2::CENTER_CENTER, format!("{max:.0}"), FontId::monospace(8.0), DIM);
    // header: ≡ name  … GAUGE  ×
    p.text(rect.left_top() + vec2(6.0, 5.0), Align2::LEFT_TOP, format!("≡ {name}"), FontId::monospace(10.0), DIM);
    p.text(rect.right_top() + vec2(-6.0, 5.0), Align2::RIGHT_TOP, "×", FontId::monospace(10.0), DIM);
    badge(p, rect.right_top() + vec2(-46.0, 4.0), "LINE"); // click -> switch to line
    p.text(
        pos2(center.x, rect.bottom() - 16.0),
        Align2::CENTER_CENTER,
        format!("{value:.2} {unit}"),
        FontId::monospace(12.0),
        CYAN,
    );
}

/// Fallback (no GPS/tileset): draw the track scaled to its own bbox.
fn draw_track_bbox(p: &egui::Painter, rect: Rect, track: &[(f64, f64)]) {
    if track.len() < 2 {
        return;
    }
    let (mut mnla, mut mxla, mut mnlo, mut mxlo) = (f64::MAX, f64::MIN, f64::MAX, f64::MIN);
    for &(la, lo) in track {
        mnla = mnla.min(la);
        mxla = mxla.max(la);
        mnlo = mnlo.min(lo);
        mxlo = mxlo.max(lo);
    }
    let plot = Rect::from_min_max(rect.left_top() + vec2(10.0, 22.0), rect.right_bottom() - vec2(10.0, 10.0));
    let sx = (mxlo - mnlo).max(1e-9);
    let sy = (mxla - mnla).max(1e-9);
    let pts: Vec<Pos2> = track
        .iter()
        .map(|&(la, lo)| {
            pos2(
                plot.left() + ((lo - mnlo) / sx) as f32 * plot.width(),
                plot.bottom() - ((la - mnla) / sy) as f32 * plot.height(),
            )
        })
        .collect();
    p.add(egui::Shape::line(pts.clone(), Stroke::new(1.5, CYAN)));
    if let Some(last) = pts.last() {
        p.circle_filled(*last, 4.0, GREEN);
    }
}

fn fmt_ms_short(ms: i64) -> String {
    let ms = ms.max(0);
    format!("{}:{:02}", ms / 60_000, (ms / 1000) % 60)
}

/// Small dim badge (e.g. "LINE"/"GAUGE") drawn at a top-left-ish anchor.
fn badge(p: &egui::Painter, at: Pos2, text: &str) {
    let galley = p.layout_no_wrap(text.to_string(), FontId::monospace(8.0), DIM);
    let r = Rect::from_min_size(at, galley.size() + vec2(8.0, 3.0));
    p.rect_stroke(r, 2.0, Stroke::new(1.0, BORDER));
    p.text(r.center(), Align2::CENTER_CENTER, text, FontId::monospace(8.0), DIM);
}

fn paint_line(p: &egui::Painter, rect: Rect, w: &Widget) {
    let grid = Color32::from_rgb(0x14, 0x1d, 0x28);
    p.rect_filled(rect, 3.0, CARD);
    p.rect_stroke(rect, 3.0, Stroke::new(1.0, BORDER));
    let last = w.points.last().map(|x| x.1);
    let val = last.map(|v| format!("{v:.3}")).unwrap_or_else(|| "—".into());
    p.text(rect.left_top() + vec2(6.0, 5.0), Align2::LEFT_TOP, format!("≡ {}", w.name), FontId::monospace(10.0), DIM);
    p.text(rect.right_top() + vec2(-6.0, 5.0), Align2::RIGHT_TOP, "×", FontId::monospace(10.0), DIM);
    badge(p, rect.left_top() + vec2(100.0, 4.0), "GAUGE"); // click -> switch to gauge
    p.text(
        rect.right_top() + vec2(-16.0, 5.0),
        Align2::RIGHT_TOP,
        format!("{val} {}", w.unit),
        FontId::monospace(11.0),
        CYAN,
    );
    let plot = Rect::from_min_max(rect.left_top() + vec2(36.0, 24.0), rect.right_bottom() - vec2(8.0, 15.0));
    for i in 0..=4 {
        let f = i as f32 / 4.0;
        let y = plot.top() + f * plot.height();
        let vy = w.max - (f as f64) * (w.max - w.min);
        p.line_segment([pos2(plot.left(), y), pos2(plot.right(), y)], Stroke::new(1.0, grid));
        p.text(pos2(plot.left() - 4.0, y), Align2::RIGHT_CENTER, format!("{vy:.0}"), FontId::monospace(8.0), DIM);
    }
    if w.points.len() >= 2 {
        let win = (WINDOW_MS as f64 / w.zoom).max(1000.0) as f32; // zoomed visible window
        let newest = w.points[w.points.len() - 1].0;
        for i in 0..=3 {
            let f = i as f32 / 3.0;
            let x = plot.left() + f * plot.width();
            let ts = newest - ((1.0 - f) * win) as i64;
            p.line_segment([pos2(x, plot.top()), pos2(x, plot.bottom())], Stroke::new(1.0, grid));
            p.text(pos2(x, plot.bottom() + 2.0), Align2::CENTER_TOP, fmt_ms_short(ts), FontId::monospace(8.0), DIM);
        }
        let span = (w.max - w.min).max(1e-9);
        let poly: Vec<Pos2> = w
            .points
            .iter()
            .filter(|&&(ts, _)| (newest - ts) as f32 <= win)
            .map(|&(ts, v)| {
                let age = (newest - ts) as f32;
                let x = plot.right() - (age / win) * plot.width();
                let norm = ((v - w.min) / span).clamp(0.0, 1.0) as f32;
                pos2(x, plot.bottom() - norm * plot.height())
            })
            .collect();
        p.add(egui::Shape::line(poly, Stroke::new(1.5, CYAN)));
    }
}

impl eframe::App for Dash {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.tick();
        // Repaint on CHANGE, not on a fixed 30 Hz clock: while playing, wake at the
        // data cadence (~10 Hz at SPEED=1) for the next frame; while paused, don't
        // schedule anything — egui still repaints on input (hover/drag/pan/zoom), so
        // an idle paused window costs ~0% instead of re-rendering the map 30×/s.
        if self.playing && self.ride_ms < self.total_ms as f64 {
            let ms = (100.0 / self.speed).clamp(16.0, 1000.0) as u64;
            ctx.request_repaint_after(Duration::from_millis(ms));
        }
        let clock = fmt_clock(self.ride_ms as i64);

        // ---- Top bar ----
        egui::TopBottomPanel::top("bar")
            .exact_height(44.0)
            .show_separator_line(false) // no light seam between panels
            .frame(egui::Frame::none().fill(PANEL).inner_margin(egui::Margin::symmetric(12.0, 0.0)))
            .show(ctx, |ui| {
                // frameless: the top bar itself drags the window (empty areas only —
                // tabs/buttons are added after, so they win the hit-test where they overlap).
                let bar_rect = ui.max_rect();
                let wdrag = ui.interact(bar_rect, ui.id().with("wdrag"), egui::Sense::click_and_drag());
                if wdrag.drag_started() {
                    ctx.send_viewport_cmd(egui::ViewportCommand::StartDrag);
                }
                if wdrag.double_clicked() {
                    let max = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
                    ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!max));
                }
                ui.horizontal_centered(|ui| {
                    // logo: small cyan square mark + stacked wordmark
                    let (sq, _) = ui.allocate_exact_size(vec2(16.0, 16.0), egui::Sense::hover());
                    ui.painter().rect_filled(Rect::from_center_size(sq.center(), vec2(15.0, 15.0)), 3.0, CYAN);
                    ui.add_space(8.0);
                    ui.vertical(|ui| {
                        ui.add_space(5.0);
                        ui.label(RichText::new("INU·MONITOR").color(CYAN).strong().size(14.0));
                        ui.label(RichText::new("INERTIAL NAV TELEMETRY v4.0").color(DIM).size(8.0));
                    });
                    ui.add_space(14.0);
                    ui.colored_label(Color32::from_rgb(0x8f, 0xa3, 0xb3), RichText::new("AC 4X-ELT / FLT 1182").size(12.0));
                    ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                        // frameless window controls (rightmost): vector-drawn so they render
                        // identically across platforms (bundled fonts lack ✕/▢ glyphs → tofu).
                        // right_to_left: first allocated is rightmost, so close comes first.
                        let win_icon = |ui: &mut egui::Ui, kind: u8| -> bool {
                            let (r, resp) = ui.allocate_exact_size(vec2(22.0, 22.0), egui::Sense::click());
                            let col = if resp.hovered() { TEXT } else { DIM };
                            let p = ui.painter_at(r);
                            let c = r.center();
                            let s = 4.0;
                            match kind {
                                0 => {
                                    p.line_segment([pos2(c.x - s, c.y - s), pos2(c.x + s, c.y + s)], Stroke::new(1.2, col));
                                    p.line_segment([pos2(c.x + s, c.y - s), pos2(c.x - s, c.y + s)], Stroke::new(1.2, col));
                                }
                                1 => {
                                    p.rect_stroke(Rect::from_center_size(c, vec2(s * 2.0, s * 2.0)), 0.0, Stroke::new(1.2, col));
                                }
                                _ => {
                                    p.line_segment([pos2(c.x - s, c.y + 3.0), pos2(c.x + s, c.y + 3.0)], Stroke::new(1.2, col));
                                }
                            }
                            resp.clicked()
                        };
                        if win_icon(ui, 0) {
                            ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                        }
                        if win_icon(ui, 1) {
                            let max = ctx.input(|i| i.viewport().maximized.unwrap_or(false));
                            ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(!max));
                        }
                        if win_icon(ui, 2) {
                            ctx.send_viewport_cmd(egui::ViewportCommand::Minimized(true));
                        }
                        ui.add_space(12.0);
                        ui.colored_label(TEXT, RichText::new(&clock).monospace().size(12.0));
                        ui.add_space(10.0);
                        ui.colored_label(GREEN, RichText::new("● LINK 1553B·OK").size(11.0));
                        ui.add_space(10.0);
                        ui.colored_label(DIM, RichText::new("SCALES ON").size(10.0));
                        ui.add_space(8.0);
                        let ca = self.cautions();
                        pill(ui, AMBER, &format!("● {ca} CAUTION"));
                        ui.add_space(6.0);
                        let al = self.alarms();
                        pill(ui, RED, &format!("● {al} ALARM"));
                    });
                });
                // tabs centered in the bar (absolute, painted over the row)
                {
                    let font = FontId::proportional(13.0);
                    let labels = [("OVERVIEW", Tab::Overview), ("FLIGHT TRACK", Tab::FlightTrack), ("EVENTS", Tab::Events)];
                    let gap = 22.0;
                    let widths: Vec<f32> = labels
                        .iter()
                        .map(|(l, _)| ui.fonts(|f| f.layout_no_wrap(l.to_string(), font.clone(), DIM).size().x))
                        .collect();
                    let total_w: f32 = widths.iter().sum::<f32>() + gap * (labels.len() - 1) as f32;
                    let cy = bar_rect.center().y;
                    let mut x = bar_rect.center().x - total_w / 2.0;
                    for (i, (label, t)) in labels.iter().enumerate() {
                        let w = widths[i];
                        let r = Rect::from_min_size(pos2(x, cy - 10.0), vec2(w, 20.0));
                        let resp = ui.interact(r, ui.id().with(("tab", i)), egui::Sense::click());
                        let active = self.tab == *t;
                        let col = if active || resp.hovered() { CYAN } else { DIM };
                        ui.painter().text(pos2(x, cy), Align2::LEFT_CENTER, *label, font.clone(), col);
                        if active {
                            ui.painter().line_segment([pos2(x, cy + 11.0), pos2(x + w, cy + 11.0)], Stroke::new(2.0, CYAN));
                        }
                        if resp.clicked() {
                            self.tab = *t;
                        }
                        x += w + gap;
                    }
                }
            });

        // ---- Bottom transport / status bar ----
        // Layout mirrors the Tauri/.NET transport: a control row (buttons + stacked
        // clock/T+·speed), a full-width seek bar, then a stacked BUFFER/SAMPLES/DROPPED row.
        let total = self.total_ms.max(1);
        let played = (self.ride_ms / total as f64).clamp(0.0, 1.0) as f32;
        egui::TopBottomPanel::bottom("transport")
            .exact_height(96.0)
            .show_separator_line(false)
            .frame(egui::Frame::none().fill(PANEL).inner_margin(egui::Margin::symmetric(16.0, 8.0)))
            .show(ctx, |ui| {
                ui.horizontal(|ui| {
                    // rewind-to-start (bar + two triangles)
                    let (rw, rwr) = ui.allocate_exact_size(vec2(22.0, 18.0), egui::Sense::click());
                    {
                        let p = ui.painter_at(rw);
                        let c = rw.center();
                        p.line_segment([pos2(c.x - 8.0, c.y - 6.0), pos2(c.x - 8.0, c.y + 6.0)], Stroke::new(1.5, CYAN));
                        p.add(egui::Shape::convex_polygon(vec![pos2(c.x - 1.0, c.y - 6.0), pos2(c.x - 7.0, c.y), pos2(c.x - 1.0, c.y + 6.0)], CYAN, Stroke::NONE));
                        p.add(egui::Shape::convex_polygon(vec![pos2(c.x + 6.0, c.y - 6.0), pos2(c.x, c.y), pos2(c.x + 6.0, c.y + 6.0)], CYAN, Stroke::NONE));
                    }
                    if rwr.clicked() {
                        self.seek(0);
                    }
                    // play / pause
                    let (pp, ppr) = ui.allocate_exact_size(vec2(22.0, 18.0), egui::Sense::click());
                    {
                        let p = ui.painter_at(pp);
                        let c = pp.center();
                        if self.playing {
                            p.rect_filled(Rect::from_center_size(pos2(c.x - 3.0, c.y), vec2(3.0, 13.0)), 0.0, CYAN);
                            p.rect_filled(Rect::from_center_size(pos2(c.x + 3.0, c.y), vec2(3.0, 13.0)), 0.0, CYAN);
                        } else {
                            p.add(egui::Shape::convex_polygon(vec![pos2(c.x - 5.0, c.y - 6.0), pos2(c.x + 6.0, c.y), pos2(c.x - 5.0, c.y + 6.0)], CYAN, Stroke::NONE));
                        }
                    }
                    if ppr.clicked() {
                        self.playing = !self.playing;
                    }
                    // forward-to-end
                    let (fw, fwr) = ui.allocate_exact_size(vec2(22.0, 18.0), egui::Sense::click());
                    {
                        let p = ui.painter_at(fw);
                        let c = fw.center();
                        p.add(egui::Shape::convex_polygon(vec![pos2(c.x - 6.0, c.y - 6.0), pos2(c.x, c.y), pos2(c.x - 6.0, c.y + 6.0)], CYAN, Stroke::NONE));
                        p.add(egui::Shape::convex_polygon(vec![pos2(c.x + 1.0, c.y - 6.0), pos2(c.x + 7.0, c.y), pos2(c.x + 1.0, c.y + 6.0)], CYAN, Stroke::NONE));
                        p.line_segment([pos2(c.x + 8.0, c.y - 6.0), pos2(c.x + 8.0, c.y + 6.0)], Stroke::new(1.5, CYAN));
                    }
                    if fwr.clicked() {
                        self.seek(self.total_ms);
                    }
                    ui.add_space(16.0);
                    // clock (big) + T+/speed subtitle, stacked
                    ui.vertical(|ui| {
                        ui.add_space(1.0);
                        ui.label(RichText::new(&clock).color(TEXT).monospace().size(15.0));
                        ui.colored_label(DIM, RichText::new(format!("T+{clock} · {:.1} s/s", self.speed)).monospace().size(9.0));
                    });
                });
                ui.add_space(6.0);
                // interactive seek bar
                let (bar, resp) =
                    ui.allocate_exact_size(vec2(ui.available_width(), 10.0), egui::Sense::click_and_drag());
                if (resp.clicked() || resp.dragged()) && bar.width() > 0.0 {
                    if let Some(pos) = resp.interact_pointer_pos() {
                        let frac = ((pos.x - bar.left()) / bar.width()).clamp(0.0, 1.0) as f64;
                        self.seek((frac * total as f64) as i64);
                    }
                }
                let p = ui.painter_at(bar);
                let y = bar.center().y;
                // rounded track + cyan progress fill + knob
                let track = Rect::from_min_max(pos2(bar.left(), y - 2.0), pos2(bar.right(), y + 2.0));
                p.rect_filled(track, 2.0, Color32::from_rgb(0x1c, 0x27, 0x33));
                let hx = bar.left() + played * bar.width();
                p.rect_filled(Rect::from_min_max(pos2(bar.left(), y - 2.0), pos2(hx, y + 2.0)), 2.0, CYAN);
                p.circle_filled(pos2(hx, y), 5.0, CYAN);
                p.circle_stroke(pos2(hx, y), 5.0, Stroke::new(1.0, Color32::from_rgb(0x0a, 0x0e, 0x14)));
                ui.add_space(8.0);
                // stacked stats row: label (9, dim) over value (11); DROPPED value green
                ui.horizontal(|ui| {
                    let stat = |ui: &mut egui::Ui, label: &str, val: String, col: Color32| {
                        ui.vertical(|ui| {
                            ui.colored_label(DIM, RichText::new(label).monospace().size(9.0));
                            ui.colored_label(col, RichText::new(val).monospace().size(11.0));
                        });
                    };
                    stat(ui, "BUFFER", clock.clone(), TEXT);
                    ui.add_space(24.0);
                    stat(ui, "SAMPLES", format!("{}", self.cursor), TEXT);
                    ui.add_space(24.0);
                    stat(ui, "DROPPED", "0".into(), GREEN);
                });
            });

        // ---- Param table (grouped) ----
        egui::SidePanel::left("params")
            .exact_width(300.0)
            .resizable(false)
            .show_separator_line(false)
            .frame(egui::Frame::none().fill(PANEL))
            .show(ctx, |ui| {
                egui::Frame::none()
                    .inner_margin(egui::Margin::symmetric(12.0, 8.0))
                    .show(ui, |ui| {
                        ui.horizontal(|ui| {
                            ui.colored_label(GREEN, "●");
                            ui.colored_label(CYAN, RichText::new("PARAMETERS").strong());
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                ui.colored_label(DIM, RichText::new(format!("ALL {} CH", self.channels.len())).small());
                            });
                        });
                        ui.add_space(2.0);
                        ui.horizontal(|ui| {
                            ui.colored_label(BORDER, RichText::new("PARAMETER").small());
                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                ui.colored_label(BORDER, RichText::new("BUS").small());
                                ui.add_space(8.0);
                                ui.colored_label(BORDER, RichText::new("ENG-DATA").small());
                            });
                        });
                    });
                // drag_to_scroll off so dragging a row starts a dnd, not a scroll
                egui::ScrollArea::vertical().drag_to_scroll(false).show(ui, |ui| {
                    egui::Frame::none()
                        .inner_margin(egui::Margin::symmetric(12.0, 0.0))
                        .show(ui, |ui| {
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
                                // group header strip (full-width dark bg)
                                let (bg, _) = ui.allocate_exact_size(vec2(ui.available_width(), 17.0), egui::Sense::hover());
                                let gp = ui.painter_at(bg);
                                gp.rect_filled(bg, 0.0, Color32::from_rgb(0x0d, 0x16, 0x21));
                                gp.text(bg.left_center() + vec2(2.0, 0.0), Align2::LEFT_CENTER, gname.to_uppercase(), FontId::monospace(9.0), Color32::from_rgb(0x6f, 0x84, 0x96));
                                gp.text(bg.right_center() - vec2(2.0, 0.0), Align2::RIGHT_CENTER, rows.len().to_string(), FontId::monospace(9.0), BORDER);
                                for (col, c) in rows {
                                    let (text, color) = self.fmt_param(col, c.type_ == "enum", c.id);
                                    // drag a row into the grid to add a chart for that channel
                                    ui.dnd_drag_source(egui::Id::new(("param", c.id)), col, |ui| {
                                        ui.horizontal(|ui| {
                                            let (dot, _) = ui.allocate_exact_size(vec2(8.0, 8.0), egui::Sense::hover());
                                            ui.painter().circle_filled(dot.center(), 3.0, if color == RED { RED } else { GREEN });
                                            ui.label(RichText::new(&c.name).color(DIM).small());
                                            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                                                ui.label(RichText::new(&c.addr).color(BORDER).small());
                                                ui.add_space(4.0);
                                                ui.colored_label(color, RichText::new(text).small());
                                            });
                                        });
                                    });
                                }
                            }
                        });
                });
            });

        // ---- Central: Flight Track + interactive widget grid ----
        // Click a widget's LINE/GAUGE badge to toggle its kind; click × to remove.
        egui::CentralPanel::default()
            .frame(egui::Frame::none().fill(BG).inner_margin(egui::Margin::same(12.0)))
            .show(ctx, |ui| match self.tab {
                Tab::Overview => {
                egui::ScrollArea::vertical().show(ui, |ui| {
                    let area = ui.available_rect_before_wrap();
                    let origin = area.min;
                    let cell_w = (area.width() / GRID_COLS as f32).max(90.0);
                    let cell_h = 165.0;
                    let gap = 6.0;
                    let max_row = self.widgets.iter().map(|w| w.gy + w.gh).max().unwrap_or(1);
                    // fill the whole area (not just the widget rows) so there is empty
                    // droppable space below the grid for adding a widget via DnD.
                    ui.allocate_space(vec2(area.width(), area.height().max(max_row as f32 * cell_h + 8.0)));

                    let drag = self.drag;
                    let mut new_drag = drag;
                    let mut act: Option<GridAct> = None;
                    let mut map_rect: Option<Rect> = None;
                    // dashed placeholder (like .NET's DropGhost): the widget itself stays
                    // put while dragging; the ghost shows the snapped landing / resized size.
                    let mut ghost: Option<Rect> = None;
                    for i in 0..self.widgets.len() {
                        let w = &self.widgets[i];
                        let base = Rect::from_min_size(
                            origin + vec2(w.gx as f32 * cell_w + gap, w.gy as f32 * cell_h + gap),
                            vec2(w.gw as f32 * cell_w - 2.0 * gap, w.gh as f32 * cell_h - 2.0 * gap),
                        );
                        // widget draws at its static cell; drag only moves the ghost
                        let rect = base;
                        // Map is a FIXED panning/zooming widget (like Tauri) — no grid
                        // move/resize so drag/scroll inside it pan & zoom the map. Drawn
                        // (interactive) after the loop since it borrows &mut self.
                        if w.kind == Kind::Map {
                            map_rect = Some(rect);
                            continue;
                        }
                        let id = ui.id().with(("w", i));
                        let resp = ui.interact(rect, id, egui::Sense::click_and_drag());
                        let p = ui.painter_at(rect);
                        match w.kind {
                            Kind::Line => paint_line(&p, rect, w),
                            Kind::Gauge => {
                                let v = self.latest.as_ref().and_then(|s| s.values.get(w.col)).copied().unwrap_or(w.min);
                                draw_gauge(&p, rect, v, w.min, w.max, &w.name, &w.unit);
                            }
                            Kind::Map => {}
                        }
                        // hover tooltip: value at the cursor's time on a line chart
                        if w.kind == Kind::Line && w.points.len() >= 2 {
                            if let Some(pos) = resp.hover_pos() {
                                let plot = Rect::from_min_max(rect.left_top() + vec2(36.0, 24.0), rect.right_bottom() - vec2(8.0, 15.0));
                                if plot.contains(pos) {
                                    // use the SAME zoomed window as paint_line, else the readout
                                    // dot drifts off the line when the chart is zoomed
                                    let win = (WINDOW_MS as f64 / w.zoom).max(1000.0) as f32;
                                    let newest = w.points[w.points.len() - 1].0;
                                    let frac = ((pos.x - plot.left()) / plot.width()).clamp(0.0, 1.0);
                                    let ts_c = newest - ((1.0 - frac) * win) as i64;
                                    if let Some(&(ts, v)) = w
                                        .points
                                        .iter()
                                        .filter(|&&(t, _)| (newest - t) as f32 <= win)
                                        .min_by_key(|(t, _)| (*t - ts_c).abs())
                                    {
                                        let px = plot.right() - ((newest - ts) as f32 / win) * plot.width();
                                        let span = (w.max - w.min).max(1e-9);
                                        let py = plot.bottom() - (((v - w.min) / span).clamp(0.0, 1.0) as f32) * plot.height();
                                        p.line_segment([pos2(px, plot.top()), pos2(px, plot.bottom())], Stroke::new(1.0, DIM));
                                        p.circle_filled(pos2(px, py), 3.0, CYAN);
                                        let label = format!("{} · {:.3} {}", fmt_ms_short(ts), v, w.unit);
                                        let gal = p.layout_no_wrap(label.clone(), FontId::monospace(9.0), TEXT);
                                        let tip = Rect::from_min_size(pos2(px + 6.0, plot.top() + 2.0), gal.size() + vec2(8.0, 4.0));
                                        p.rect_filled(tip, 2.0, PANEL);
                                        p.rect_stroke(tip, 2.0, Stroke::new(1.0, BORDER));
                                        p.text(tip.center(), Align2::CENTER_CENTER, label, FontId::monospace(9.0), TEXT);
                                    }
                                }
                            }
                        }
                        // resize grip (bottom-right) — visual only; interaction via `resp`
                        let grip = Rect::from_min_size(rect.right_bottom() - vec2(16.0, 16.0), vec2(16.0, 16.0));
                        p.line_segment([pos2(grip.left() + 4.0, grip.bottom() - 2.0), pos2(grip.right() - 2.0, grip.top() + 4.0)], Stroke::new(1.0, DIM));
                        p.line_segment([pos2(grip.left() + 8.0, grip.bottom() - 2.0), pos2(grip.right() - 2.0, grip.top() + 8.0)], Stroke::new(1.0, DIM));

                        // corner cursors: top-left = move (SizeAll), bottom-right = resize
                        // (mirrors the .NET grip / BottomRightCorner thumb).
                        let in_br = |pp: Pos2| pp.x > base.right() - 18.0 && pp.y > base.bottom() - 18.0;
                        let in_tl = |pp: Pos2| pp.x < base.left() + 18.0 && pp.y < base.top() + 18.0;
                        if let Some(d) = drag.filter(|d| d.idx == i) {
                            ui.ctx().set_cursor_icon(if d.resize { egui::CursorIcon::ResizeNwSe } else { egui::CursorIcon::Move });
                        } else if let Some(pos) = resp.hover_pos() {
                            if in_br(pos) {
                                ui.ctx().set_cursor_icon(egui::CursorIcon::ResizeNwSe);
                            } else if in_tl(pos) {
                                ui.ctx().set_cursor_icon(egui::CursorIcon::Move);
                            }
                        }
                        // placeholder ghost while this widget is being dragged
                        if let Some(d) = drag.filter(|d| d.idx == i) {
                            ghost = Some(if d.resize {
                                Rect::from_min_size(
                                    base.min,
                                    vec2((base.width() + d.off.x).max(cell_w * 0.5), (base.height() + d.off.y).max(cell_h * 0.5)),
                                )
                            } else {
                                let moved = base.translate(d.off);
                                let ngx = (((moved.min.x - origin.x) / cell_w).round() as i32).clamp(0, GRID_COLS - w.gw);
                                let ngy = (((moved.min.y - origin.y) / cell_h).round() as i32).max(0);
                                Rect::from_min_size(origin + vec2(ngx as f32 * cell_w + gap, ngy as f32 * cell_h + gap), base.size())
                            });
                        }

                        if resp.drag_started() {
                            // resize if the press landed in the bottom-right grip, else move
                            let resize = resp
                                .interact_pointer_pos()
                                .map(|pp| pp.x > base.right() - 18.0 && pp.y > base.bottom() - 18.0)
                                .unwrap_or(false);
                            new_drag = Some(DragState { idx: i, off: resp.drag_delta(), resize, base_gw: w.gw, base_gh: w.gh });
                        } else if resp.dragged() {
                            if let Some(d) = drag {
                                if d.idx == i {
                                    new_drag = Some(DragState { off: d.off + resp.drag_delta(), ..d });
                                }
                            }
                        } else if resp.drag_stopped() {
                            if let Some(d) = drag {
                                if d.idx == i && d.resize {
                                    let nw = (d.base_gw + (d.off.x / cell_w).round() as i32).clamp(1, GRID_COLS);
                                    let nh = (d.base_gh + (d.off.y / cell_h).round() as i32).clamp(1, 3);
                                    act = Some(GridAct::Resize(i, nw, nh));
                                } else if d.idx == i {
                                    let moved = base.translate(d.off);
                                    let ngx = (((moved.min.x - origin.x) / cell_w).round() as i32).clamp(0, GRID_COLS - w.gw);
                                    let ngy = (((moved.min.y - origin.y) / cell_h).round() as i32).max(0);
                                    act = Some(GridAct::Move(i, ngx, ngy));
                                }
                            }
                            new_drag = None;
                        } else if resp.clicked() {
                            if let Some(pos) = resp.interact_pointer_pos() {
                                let close = Rect::from_min_size(rect.right_top() + vec2(-18.0, 2.0), vec2(16.0, 16.0));
                                let badge_r = match w.kind {
                                    Kind::Line => Rect::from_min_size(rect.left_top() + vec2(98.0, 3.0), vec2(44.0, 14.0)),
                                    Kind::Gauge => Rect::from_min_size(rect.right_top() + vec2(-48.0, 3.0), vec2(40.0, 14.0)),
                                    Kind::Map => Rect::NOTHING, // no LINE/GAUGE badge on the map
                                };
                                if close.contains(pos) {
                                    act = Some(GridAct::Remove(i));
                                } else if badge_r.contains(pos) {
                                    act = Some(GridAct::Toggle(i));
                                }
                            }
                        }
                        // right-click a chart -> zoom context menu
                        if w.kind == Kind::Line {
                            resp.context_menu(|ui| {
                                if ui.button("Zoom in").clicked() {
                                    act = Some(GridAct::Zoom(i, 2.0));
                                    ui.close_menu();
                                }
                                if ui.button("Zoom out").clicked() {
                                    act = Some(GridAct::Zoom(i, 0.5));
                                    ui.close_menu();
                                }
                                if ui.button("Reset zoom").clicked() {
                                    act = Some(GridAct::ResetZoom(i));
                                    ui.close_menu();
                                }
                            });
                        }
                    }
                    self.drag = new_drag;
                    match act {
                        Some(GridAct::Remove(i)) => {
                            self.widgets.remove(i);
                        }
                        Some(GridAct::Toggle(i)) => {
                            self.widgets[i].kind = match self.widgets[i].kind {
                                Kind::Line => Kind::Gauge,
                                Kind::Gauge => Kind::Line,
                                Kind::Map => Kind::Map,
                            };
                        }
                        Some(GridAct::Move(i, gx, gy)) => {
                            // Swap with whatever occupies the target cell so widgets don't
                            // stack on top of each other (no free-canvas overlap).
                            let (ox, oy) = (self.widgets[i].gx, self.widgets[i].gy);
                            if let Some(j) = (0..self.widgets.len())
                                .find(|&j| j != i && self.widgets[j].gx == gx && self.widgets[j].gy == gy)
                            {
                                self.widgets[j].gx = ox;
                                self.widgets[j].gy = oy;
                            }
                            self.widgets[i].gx = gx;
                            self.widgets[i].gy = gy;
                        }
                        Some(GridAct::Resize(i, gw, gh)) => {
                            self.widgets[i].gw = gw;
                            self.widgets[i].gh = gh;
                        }
                        Some(GridAct::Zoom(i, f)) => {
                            self.widgets[i].zoom = (self.widgets[i].zoom * f).clamp(0.25, 16.0);
                        }
                        Some(GridAct::ResetZoom(i)) => {
                            self.widgets[i].zoom = 1.0;
                        }
                        None => {}
                    }
                    // map widget: drawn last (needs &mut self; grid loop only borrowed
                    // &self.widgets). Static preview here; pan/zoom lives in FLIGHT TRACK.
                    if let Some(mr) = map_rect {
                        self.draw_map(ui, mr, true);
                    }
                    // dashed placeholder on top of everything (like .NET's DropGhost)
                    if let Some(g) = ghost {
                        let pg = ui.painter();
                        pg.rect_filled(g, 3.0, Color32::from_rgba_unmultiplied(0x38, 0xc5, 0xe0, 0x15)); // .NET DropGhost fill #1538C5E0
                        let corners = [g.left_top(), g.right_top(), g.right_bottom(), g.left_bottom(), g.left_top()];
                        for seg in corners.windows(2) {
                            for s in egui::Shape::dashed_line(&[seg[0], seg[1]], Stroke::new(1.5, CYAN), 4.0, 3.0) {
                                pg.add(s);
                            }
                        }
                    }
                });
                // manual DnD: a param row released over the grid area adds a widget
                if ui.input(|i| i.pointer.any_released()) && ui.ui_contains_pointer() {
                    if let Some(col) = egui::DragAndDrop::take_payload::<usize>(ui.ctx()) {
                        self.add_widget(*col);
                    }
                }
                }
                Tab::FlightTrack => {
                    let rect = ui.available_rect_before_wrap();
                    self.draw_map(ui, rect, true);
                }
                Tab::Events => {
                    ui.colored_label(CYAN, RichText::new("EVENTS — INU STATUS").strong());
                    ui.separator();
                    egui::ScrollArea::vertical().show(ui, |ui| {
                        let mut any = false;
                        for (col, c) in self.channels.iter().enumerate() {
                            if c.type_ != "enum" {
                                continue;
                            }
                            any = true;
                            let (text, color) = self.fmt_param(col, true, c.id);
                            ui.horizontal(|ui| {
                                let (dot, _) = ui.allocate_exact_size(vec2(10.0, 10.0), egui::Sense::hover());
                                ui.painter().circle_filled(dot.center(), 4.0, color);
                                ui.colored_label(DIM, &c.name);
                                ui.colored_label(color, RichText::new(text).strong());
                                ui.colored_label(BORDER, &c.addr);
                            });
                        }
                        if !any {
                            ui.colored_label(DIM, "No status channels.");
                        }
                    });
                }
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

fn setup_theme(ctx: &egui::Context) {
    // bundle Noto Sans Hebrew as a fallback so map labels render Israeli place/street
    // names (Hebrew `name`) like the Tauri MapLibre style, not tofu boxes.
    let mut fonts = egui::FontDefinitions::default();
    fonts.font_data.insert(
        "hebrew".to_owned(),
        egui::FontData::from_static(include_bytes!("../assets/NotoSansHebrew-Regular.ttf")),
    );
    for fam in [egui::FontFamily::Proportional, egui::FontFamily::Monospace] {
        fonts.families.entry(fam).or_default().push("hebrew".to_owned());
    }
    ctx.set_fonts(fonts);

    let mut style = (*ctx.style()).clone();
    style.override_text_style = Some(egui::TextStyle::Monospace);
    let mut v = egui::Visuals::dark();
    v.panel_fill = BG;
    v.window_fill = PANEL;
    v.extreme_bg_color = CARD;
    v.override_text_color = Some(TEXT);
    v.widgets.noninteractive.bg_stroke = Stroke::new(1.0, BORDER);
    style.visuals = v;
    ctx.set_style(style);
}

fn main() -> eframe::Result<()> {
    let db = resolve_db();
    let speed = std::env::var("RIDE_SPEED").ok().and_then(|s| s.parse().ok()).unwrap_or(1.0);
    let dash = Dash::new(&db, speed).unwrap_or_else(|e| panic!("open ride db {db}: {e}"));
    let opts = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([1400.0, 800.0])
            .with_min_inner_size([960.0, 600.0])
            .with_decorations(false), // frameless, like the Tauri/.NET INU shell
        ..Default::default()
    };
    eframe::run_native(
        "INU-EGUI",
        opts,
        Box::new(|cc| {
            setup_theme(&cc.egui_ctx);
            Ok(Box::new(dash))
        }),
    )
}
