//! Pure strip-chart geometry: (ts, value) samples -> canvas pixel points.
//! The newest sample sits at the right edge; the window's left edge is x=0.

pub fn to_screen(
    points: &[(i64, f64)],
    window_ms: i64,
    w: f32,
    h: f32,
    min: f64,
    max: f64,
) -> Vec<(f32, f32)> {
    if points.is_empty() {
        return Vec::new();
    }
    let newest = points[points.len() - 1].0;
    let span = (max - min).max(1e-9);
    points
        .iter()
        .map(|&(ts, v)| {
            let age = (newest - ts) as f32; // 0 at newest
            let x = w - (age / window_ms as f32) * w;
            let norm = ((v - min) / span).clamp(0.0, 1.0) as f32;
            let y = h - norm * h; // invert: max at top
            (x, y)
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_input_gives_no_points() {
        assert!(to_screen(&[], 1000, 100.0, 50.0, 0.0, 1.0).is_empty());
    }

    #[test]
    fn newest_point_maps_to_right_edge() {
        let pts = [(0i64, 0.0), (1000i64, 1.0)];
        let out = to_screen(&pts, 1000, 100.0, 50.0, 0.0, 1.0);
        assert!((out[1].0 - 100.0).abs() < 1e-3); // newest -> x = w
        assert!((out[0].0 - 0.0).abs() < 1e-3);    // oldest (age=window) -> x = 0
    }

    #[test]
    fn value_is_inverted_min_at_bottom_max_at_top() {
        let pts = [(0i64, 0.0), (0i64, 1.0)];
        let out = to_screen(&pts, 1000, 100.0, 50.0, 0.0, 1.0);
        assert!((out[0].1 - 50.0).abs() < 1e-3); // min -> y = h (bottom)
        assert!((out[1].1 - 0.0).abs() < 1e-3);  // max -> y = 0 (top)
    }

    #[test]
    fn value_is_clamped_to_min_max() {
        let pts = [(0i64, 5.0)]; // above max
        let out = to_screen(&pts, 1000, 100.0, 50.0, 0.0, 1.0);
        assert!((out[0].1 - 0.0).abs() < 1e-3); // clamped to max -> top
    }
}
