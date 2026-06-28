import type React from "react";
import { formatCount, formatElapsed } from "./tabs";

export function TransportBar({ clock, rideTag, rateHz, samples, elapsedMs, scrubberFrac, paused, onPlayPause, onSeek }: {
  clock: { hms: string; ms: string }; rideTag: string; rateHz: number;
  samples: number; elapsedMs: number; scrubberFrac: number;
  paused: boolean; onPlayPause: () => void; onSeek: (frac: number) => void;
}): React.JSX.Element {
  const pct = `${parseFloat(Math.min(100, Math.max(0, scrubberFrac * 100)).toFixed(2))}%`;
  return (
    <div className="transport">
      <div className="transport-ctrl">
        <div className="tbtns">
          <span className="tbtn">⏮</span>
          <span className={`tbtn${paused ? "" : " tbtn-on"}`} data-testid="play-pause" onClick={onPlayPause}>
            {paused ? "▶" : "⏸"}
          </span>
          <span className="tbtn">⏭</span>
        </div>
        <div className="mono"><div className="t-clock">{clock.hms}<span className="dim">.{clock.ms}</span></div>
          <div className="t-sub">T+{rideTag} · {rateHz.toFixed(1)} s/s</div></div>
      </div>
      <div className="transport-scrub">
        <div
          className="scrub-track"
          data-testid="scrub-track"
          style={{ cursor: "pointer" }}
          onClick={(e) => {
            const r = e.currentTarget.getBoundingClientRect();
            const frac = r.width ? Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) : 0;
            onSeek(frac);
          }}
        >
          <div className="scrub-fill" style={{ width: pct }} />
          <div className="scrub-marker" data-testid="scrub-marker" style={{ left: pct }} />
        </div>
      </div>
      <div className="transport-stats mono">
        <div><div className="t-lbl">BUFFER</div><div className="t-val">{formatElapsed(elapsedMs)}</div></div>
        <div><div className="t-lbl">SAMPLES</div><div className="t-val">{formatCount(samples)}</div></div>
        <div><div className="t-lbl">DROPPED</div><div className="t-val t-ok">0</div></div>
      </div>
    </div>
  );
}
