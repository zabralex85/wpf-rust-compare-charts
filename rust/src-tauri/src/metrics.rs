use serde::Serialize;
use sysinfo::{Pid, ProcessRefreshKind, RefreshKind, System};

#[derive(Debug, Clone, Serialize)]
pub struct Metrics {
    pub cpu_pct: f32,
    pub ram_mb: f64,
}

pub struct MetricsSampler {
    sys: System,
    pid: Pid,
}

impl Default for MetricsSampler {
    fn default() -> Self {
        Self::new()
    }
}

impl MetricsSampler {
    pub fn new() -> Self {
        let pid = Pid::from_u32(std::process::id());
        let sys = System::new_with_specifics(
            RefreshKind::new().with_processes(ProcessRefreshKind::everything()),
        );
        Self { sys, pid }
    }

    pub fn sample(&mut self) -> Metrics {
        self.sys
            .refresh_process_specifics(self.pid, ProcessRefreshKind::everything());
        match self.sys.process(self.pid) {
            Some(p) => Metrics {
                cpu_pct: p.cpu_usage(),
                ram_mb: p.memory() as f64 / 1_048_576.0,
            },
            None => Metrics { cpu_pct: 0.0, ram_mb: 0.0 },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sample_returns_finite_nonnegative_ram() {
        let mut s = MetricsSampler::new();
        let _ = s.sample();              // first read primes cpu measurement
        let m = s.sample();
        assert!(m.ram_mb > 0.0, "resident RAM should be positive");
        assert!(m.cpu_pct >= 0.0);
        assert!(m.cpu_pct.is_finite());
    }
}
