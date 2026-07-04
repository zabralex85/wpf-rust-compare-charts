//! Windowed strip-series buffer — Rust port of the Tauri frontend ringBuffer.ts.
//! Keeps only points within `window_ms` of the newest timestamp.

pub struct WindowBuffer {
    window_ms: i64,
    points: Vec<(i64, f64)>,
}

impl WindowBuffer {
    pub fn new(window_ms: i64) -> Self {
        Self { window_ms, points: Vec::new() }
    }

    pub fn push(&mut self, ts_ms: i64, value: f64) {
        self.points.push((ts_ms, value));
        let cutoff = ts_ms - self.window_ms;
        // evict from the front while older than the window's left edge
        let drop = self.points.iter().take_while(|(t, _)| *t < cutoff).count();
        if drop > 0 {
            self.points.drain(0..drop);
        }
    }

    pub fn points(&self) -> &[(i64, f64)] {
        &self.points
    }

    #[allow(dead_code)] // used by tests + kept as buffer API
    pub fn len(&self) -> usize {
        self.points.len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accumulates_within_window() {
        let mut b = WindowBuffer::new(1000);
        b.push(0, 1.0);
        b.push(500, 2.0);
        b.push(1000, 3.0);
        assert_eq!(b.len(), 3);
        assert_eq!(b.points()[0], (0, 1.0));
    }

    #[test]
    fn evicts_points_older_than_window() {
        let mut b = WindowBuffer::new(1000);
        b.push(0, 1.0);
        b.push(1001, 2.0); // cutoff = 1, so ts=0 is evicted
        assert_eq!(b.len(), 1);
        assert_eq!(b.points()[0], (1001, 2.0));
    }

    #[test]
    fn keeps_point_exactly_on_the_window_edge() {
        let mut b = WindowBuffer::new(1000);
        b.push(0, 1.0);
        b.push(1000, 2.0); // cutoff = 0, ts=0 is NOT < 0 -> kept
        assert_eq!(b.len(), 2);
    }
}
