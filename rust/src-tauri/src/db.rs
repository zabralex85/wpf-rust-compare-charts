use rusqlite::{Connection, Result};
use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
pub struct ChannelMeta {
    pub id: i64,
    pub name: String,
    pub column_name: String,
    pub unit: String,
    #[serde(rename = "type")]
    pub type_: String,
    pub min: f64,
    pub max: f64,
    pub widget: String,
    pub display_order: i64,
    pub addr: String,
}

#[derive(Debug, Clone, Serialize)]
pub struct EnumValue {
    pub channel_id: i64,
    pub code: i64,
    pub label: String,
    pub severity: String,
}

#[derive(Debug, Clone)]
pub struct Sample {
    pub ts_ms: i64,
    pub values: Vec<f64>,
}

pub fn load_channels(conn: &Connection) -> Result<Vec<ChannelMeta>> {
    let mut stmt = conn.prepare(
        "SELECT id, name, column_name, unit, type, min, max, widget, display_order, addr \
         FROM channels ORDER BY display_order",
    )?;
    let rows = stmt.query_map([], |r| {
        Ok(ChannelMeta {
            id: r.get(0)?,
            name: r.get(1)?,
            column_name: r.get(2)?,
            unit: r.get(3)?,
            type_: r.get(4)?,
            min: r.get(5)?,
            max: r.get(6)?,
            widget: r.get(7)?,
            display_order: r.get(8)?,
            addr: r.get(9)?,
        })
    })?;
    rows.collect()
}

pub fn load_enum_values(conn: &Connection) -> Result<Vec<EnumValue>> {
    let mut stmt = conn.prepare(
        "SELECT channel_id, code, label, severity FROM enum_values ORDER BY channel_id, code",
    )?;
    let rows = stmt.query_map([], |r| {
        Ok(EnumValue {
            channel_id: r.get(0)?,
            code: r.get(1)?,
            label: r.get(2)?,
            severity: r.get(3)?,
        })
    })?;
    rows.collect()
}

pub fn load_samples(conn: &Connection, channels: &[ChannelMeta]) -> Result<Vec<Sample>> {
    let cols: Vec<String> = channels.iter().map(|c| c.column_name.clone()).collect();
    let select = cols.join(", ");
    let sql = format!("SELECT ts, {} FROM samples ORDER BY ts", select);
    let mut stmt = conn.prepare(&sql)?;
    let n = channels.len();
    let rows = stmt.query_map([], |r| {
        let ts_ms: i64 = r.get(0)?;
        let mut values = Vec::with_capacity(n);
        for i in 0..n {
            // column index i+1; coerce any numeric storage to f64
            let v: f64 = r.get::<_, f64>(i + 1).or_else(|_| {
                r.get::<_, i64>(i + 1).map(|x| x as f64)
            })?;
            values.push(v);
        }
        Ok(Sample { ts_ms, values })
    })?;
    rows.collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use rusqlite::Connection;

    fn fixture() -> Connection {
        Connection::open("../../data/ride_small.db").expect("open fixture")
    }

    #[test]
    fn loads_thirty_channels_in_display_order() {
        let conn = fixture();
        let chans = load_channels(&conn).unwrap();
        assert_eq!(chans.len(), 30);
        let orders: Vec<i64> = chans.iter().map(|c| c.display_order).collect();
        let mut sorted = orders.clone();
        sorted.sort();
        assert_eq!(orders, sorted, "rows must come back in display order");
        assert_eq!(chans[0].id, 1);
    }

    #[test]
    fn loads_inu_mode2_enum_values() {
        let conn = fixture();
        let evs = load_enum_values(&conn).unwrap();
        let labels: Vec<&str> = evs.iter().map(|e| e.label.as_str()).collect();
        assert!(labels.contains(&"Normal"));
        assert!(labels.contains(&"Critical"));
    }

    #[test]
    fn loads_samples_in_ts_order_with_one_value_per_channel() {
        let conn = fixture();
        let chans = load_channels(&conn).unwrap();
        let samples = load_samples(&conn, &chans).unwrap();
        assert_eq!(samples.len(), 100);
        assert_eq!(samples[0].ts_ms, 0);
        assert_eq!(samples[1].ts_ms, 100);
        assert!(samples.iter().all(|s| s.values.len() == chans.len()));
        // monotonic ts
        assert!(samples.windows(2).all(|w| w[1].ts_ms > w[0].ts_ms));
    }
}
