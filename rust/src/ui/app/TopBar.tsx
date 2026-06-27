import type React from "react";
import { SCREENS, type Screen } from "./tabs";
import type { SystemStatus } from "./status";

export function TopBar({ clock, status, screen, onScreen, scalesOn, onToggleScales }: {
  clock: { hms: string; ms: string }; status: SystemStatus; screen: Screen;
  onScreen: (s: Screen) => void; scalesOn: boolean; onToggleScales: () => void;
}): React.JSX.Element {
  return (
    <div className="topbar">
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
      </div>
    </div>
  );
}
