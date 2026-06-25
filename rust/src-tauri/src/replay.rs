pub struct Pacer {
    speed: f64,
}

impl Pacer {
    pub fn new(speed: f64) -> Self {
        let speed = if speed <= 0.0 { 1.0 } else { speed };
        Self { speed }
    }

    pub fn due_offset_ms(&self, sample_ts_ms: i64) -> i64 {
        (sample_ts_ms as f64 / self.speed) as i64
    }

    pub fn wait_ms(&self, sample_ts_ms: i64, elapsed_ms: i64) -> i64 {
        (self.due_offset_ms(sample_ts_ms) - elapsed_ms).max(0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn realtime_due_offset_equals_ts() {
        let p = Pacer::new(1.0);
        assert_eq!(p.due_offset_ms(0), 0);
        assert_eq!(p.due_offset_ms(100), 100);
        assert_eq!(p.due_offset_ms(43_200_000), 43_200_000);
    }

    #[test]
    fn fast_forward_compresses_time() {
        let p = Pacer::new(10.0);
        assert_eq!(p.due_offset_ms(1000), 100);
    }

    #[test]
    fn wait_never_negative_and_accounts_for_elapsed() {
        let p = Pacer::new(1.0);
        assert_eq!(p.wait_ms(100, 0), 100);
        assert_eq!(p.wait_ms(100, 40), 60);
        assert_eq!(p.wait_ms(100, 500), 0); // behind schedule -> no wait
    }
}
