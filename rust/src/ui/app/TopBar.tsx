import type React from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { SCREENS, type Screen } from "./tabs";
import type { SystemStatus } from "./status";

// Window controls only act inside the Tauri webview; in a plain browser (dev/e2e)
// they render but no-op.
const inTauri = (): boolean =>
  typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
const winAction =
  (fn: (w: ReturnType<typeof getCurrentWindow>) => void) => (): void => {
    if (inTauri()) {
      try {
        fn(getCurrentWindow());
      } catch {
        /* not in tauri */
      }
    }
  };

export function TopBar({ clock, status, screen, onScreen, scalesOn, onToggleScales }: {
  clock: { hms: string; ms: string }; status: SystemStatus; screen: Screen;
  onScreen: (s: Screen) => void; scalesOn: boolean; onToggleScales: () => void;
}): React.JSX.Element {
  return (
    <div className="topbar" data-tauri-drag-region>
      <div className="topbar-left">
        <div className="brand"><span className="brand-mark" /><div>
          <div className="brand-name">INU&middot;MONITOR</div>
          <div className="brand-sub mono">INERTIAL NAV TELEMETRY v4.0</div>
        </div></div>
        <div className="topbar-div" />
        <div className="mono ac-id">AC 4X-ELT <span className="dim">/</span> FLT 1182</div>
      </div>
      <div className="tabs">
        {SCREENS.map((s) => (
          <div key={s.key} className={`tab${screen === s.key ? " tab-on" : ""}`} onClick={() => onScreen(s.key)}>{s.label}</div>
        ))}
      </div>
      <div className="topbar-right">
        <div className="pill pill-alarm"><span className="dot dot-alarm" />{status.alarms} ALARM</div>
        <div className="pill pill-caution"><span className="dot dot-caution" />{status.cautions} CAUTION</div>
        <div className="scales" onClick={onToggleScales}>{scalesOn ? "SCALES ON" : "SCALES OFF"}</div>
        <div className="topbar-div" />
        <div className="link"><span className={`dot ${status.linkOk ? "dot-ok" : "dot-dim"}`} />LINK <span className="mono link-ok">{status.linkOk ? "1553B·OK" : "—"}</span></div>
        <div className="clock mono"><div className="clock-hms">{clock.hms}<span className="dim">.{clock.ms}</span></div></div>
        <div className="win-ctrls">
          <span className="win-btn" aria-label="minimize" onClick={winAction((w) => w.minimize())}>&#9472;</span>
          <span className="win-btn" aria-label="maximize" onClick={winAction((w) => w.toggleMaximize())}>&#9633;</span>
          <span className="win-btn win-close" aria-label="close" onClick={winAction((w) => w.close())}>&#10005;</span>
        </div>
      </div>
    </div>
  );
}
