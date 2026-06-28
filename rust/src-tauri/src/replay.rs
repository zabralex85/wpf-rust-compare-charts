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

    /// New `t0` (wall-clock base, ms) so a sample at `target_ts_ms` is due *now*:
    /// `now_ms - due_offset(target)`. Used on seek to rebase the replay clock.
    pub fn rebase_for_seek(&self, now_ms: i64, target_ts_ms: i64) -> i64 {
        now_ms - self.due_offset_ms(target_ts_ms)
    }

    /// New `t0` after a pause: shift the base forward by the paused duration so
    /// no frames become "due" from the freeze.
    pub fn rebase_for_pause(&self, t0: i64, paused_ms: i64) -> i64 {
        t0 + paused_ms
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

    #[test]
    fn rebase_for_seek_makes_target_due_now() {
        let p = Pacer::new(2.0); // due_offset = ts/2
        assert_eq!(p.rebase_for_seek(5000, 1000), 4500); // 5000 - 1000/2
        let p1 = Pacer::new(1.0);
        assert_eq!(p1.rebase_for_seek(5000, 1000), 4000);
    }

    #[test]
    fn rebase_for_pause_shifts_base_forward() {
        let p = Pacer::new(2.0);
        assert_eq!(p.rebase_for_pause(4500, 800), 5300);
    }
}
