#[derive(serde::Deserialize)]
struct Tagged {
    #[serde(rename = "type")]
    kind: String,
}

#[derive(serde::Deserialize)]
pub struct CommandMessage {
    pub action: String,
    #[serde(default)]
    pub ts_ms: Option<i64>,
}

pub fn parse_command(text: &str) -> Option<CommandMessage> {
    let tag: Tagged = serde_json::from_str(text).ok()?;
    if tag.kind != "cmd" {
        return None;
    }
    serde_json::from_str(text).ok()
}

#[derive(Default)]
pub struct Control {
    pub paused: bool,
    pub seek_to: Option<i64>,
}

impl Control {
    pub fn apply(&mut self, cmd: &CommandMessage) {
        match cmd.action.as_str() {
            "pause" => self.paused = true,
            "resume" => self.paused = false,
            // Seek preserves the paused state: scrubbing while paused jumps to the
            // new time and stays frozen (the loop emits one frame at the target).
            "seek" => self.seek_to = cmd.ts_ms,
            _ => {}
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_and_applies() {
        assert!(parse_command("not json").is_none());
        assert!(parse_command(r#"{"type":"frame"}"#).is_none()); // not a cmd
        let mut c = Control::default();
        c.apply(&parse_command(r#"{"type":"cmd","action":"pause"}"#).unwrap());
        assert!(c.paused);
        c.apply(&parse_command(r#"{"type":"cmd","action":"resume"}"#).unwrap());
        assert!(!c.paused);
        c.apply(&parse_command(r#"{"type":"cmd","action":"seek","ts_ms":4200}"#).unwrap());
        assert_eq!(c.seek_to, Some(4200));
        assert!(!c.paused);
        // unknown action ignored
        let before = (c.paused, c.seek_to);
        c.apply(&parse_command(r#"{"type":"cmd","action":"wat"}"#).unwrap());
        assert_eq!((c.paused, c.seek_to), before);
    }
}
