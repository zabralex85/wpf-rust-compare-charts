//! In-process replay feed: loads the ride once, hands out samples as they
//! become "due" for a given elapsed wall time. No socket (mirrors the .NET
//! app's in-process idiom). Deterministic — the caller supplies elapsed_ms.

use app_lib::db::{load_channels, load_enum_values, load_samples, ChannelMeta, EnumValue, Sample};
use app_lib::replay::Pacer;
use rusqlite::Connection;

pub struct Feed {
    channels: Vec<ChannelMeta>,
    enums: Vec<EnumValue>,
    samples: Vec<Sample>,
    strip_idx: Vec<usize>,
    pacer: Pacer,
    cursor: usize,
}

impl Feed {
    pub fn open(db_path: &str, speed: f64) -> rusqlite::Result<Self> {
        let conn = Connection::open(db_path)?;
        let channels = load_channels(&conn)?;
        let enums = load_enum_values(&conn)?;
        let samples = load_samples(&conn, &channels)?;
        let strip_idx = channels
            .iter()
            .enumerate()
            .filter(|(_, c)| c.widget == "strip")
            .map(|(i, _)| i)
            .collect();
        Ok(Self { channels, enums, samples, strip_idx, pacer: Pacer::new(speed), cursor: 0 })
    }

    pub fn channels(&self) -> &[ChannelMeta] {
        &self.channels
    }

    /// Enum code→label/severity rows, for decoding enum-typed param values
    /// (mirrors the Tauri store's enum index).
    pub fn enum_values(&self) -> &[EnumValue] {
        &self.enums
    }

    pub fn strip_indices(&self) -> &[usize] {
        &self.strip_idx
    }

    pub fn due_upto(&mut self, elapsed_ms: i64) -> &[Sample] {
        let start = self.cursor;
        while self.cursor < self.samples.len()
            && self.pacer.due_offset_ms(self.samples[self.cursor].ts_ms) <= elapsed_ms
        {
            self.cursor += 1;
        }
        &self.samples[start..self.cursor]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const FIXTURE: &str = "../data/ride_small.db";

    #[test]
    fn loads_thirty_channels_with_five_strips() {
        let f = Feed::open(FIXTURE, 1.0).unwrap();
        assert_eq!(f.channels().len(), 30);
        assert_eq!(f.strip_indices().len(), 5); // roll,pitch,acc_x,acc_y,acc_z
    }

    #[test]
    fn due_upto_yields_samples_up_to_elapsed_at_speed_1() {
        let mut f = Feed::open(FIXTURE, 1.0).unwrap();
        // 10 Hz fixture: ts = 0,100,200,... at speed 1 due_offset == ts.
        let batch = f.due_upto(250).len(); // ts 0,100,200 due
        assert_eq!(batch, 3);
        let more = f.due_upto(500).len(); // ts 300,400,500
        assert_eq!(more, 3);
    }

    #[test]
    fn cursor_does_not_replay_consumed_samples() {
        let mut f = Feed::open(FIXTURE, 1.0).unwrap();
        let _ = f.due_upto(1_000_000); // drain all
        assert_eq!(f.due_upto(1_000_000).len(), 0);
    }
}
