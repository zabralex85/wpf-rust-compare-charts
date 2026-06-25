use serde::Serialize;
use crate::db::{ChannelMeta, EnumValue};

#[derive(Debug, Clone, Serialize)]
pub struct MetaMessage {
    #[serde(rename = "type")]
    pub type_: &'static str,
    pub channels: Vec<ChannelMeta>,
    pub enum_values: Vec<EnumValue>,
    pub rate_hz: i64,
}

impl MetaMessage {
    pub fn new(channels: Vec<ChannelMeta>, enum_values: Vec<EnumValue>, rate_hz: i64) -> Self {
        Self { type_: "meta", channels, enum_values, rate_hz }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct FrameMessage {
    #[serde(rename = "type")]
    pub type_: &'static str,
    pub ts_ms: i64,
    pub emit_unix_ms: i64,
    pub values: Vec<f64>,
}

impl FrameMessage {
    pub fn new(ts_ms: i64, emit_unix_ms: i64, values: Vec<f64>) -> Self {
        Self { type_: "frame", ts_ms, emit_unix_ms, values }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn frame_serializes_with_type_tag() {
        let f = FrameMessage::new(100, 1_700_000_000_000, vec![1.5, 2.0]);
        let json = serde_json::to_string(&f).unwrap();
        let v: serde_json::Value = serde_json::from_str(&json).unwrap();
        assert_eq!(v["type"], "frame");
        assert_eq!(v["ts_ms"], 100);
        assert_eq!(v["emit_unix_ms"], 1_700_000_000_000i64);
        assert_eq!(v["values"][0], 1.5);
    }

    #[test]
    fn meta_serializes_with_type_tag() {
        let m = MetaMessage::new(vec![], vec![], 10);
        let v: serde_json::Value = serde_json::from_str(&serde_json::to_string(&m).unwrap()).unwrap();
        assert_eq!(v["type"], "meta");
        assert_eq!(v["rate_hz"], 10);
    }
}
