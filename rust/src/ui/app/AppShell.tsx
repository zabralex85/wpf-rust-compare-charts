import { useState } from "react";
import type React from "react";
import "@fontsource/ibm-plex-sans/400.css";
import "@fontsource/ibm-plex-sans/500.css";
import "@fontsource/ibm-plex-sans/600.css";
import "@fontsource/ibm-plex-sans/700.css";
import "@fontsource/ibm-plex-mono/400.css";
import "@fontsource/ibm-plex-mono/500.css";
import "@fontsource/ibm-plex-mono/600.css";
import "./theme.css";
import { useTelemetry } from "../useTelemetry";
import { TopBar } from "./TopBar";
import { TransportBar } from "./TransportBar";
import { OverviewView } from "./views/OverviewView";
import { TrackView } from "./views/TrackView";
import { EventsView } from "./views/EventsView";
import { formatRideTag } from "./clock";
import { deriveStatus } from "./status";
import type { Screen } from "./tabs";
import { rideClock, rideProgress } from "./ridetime";
import { encodeCmd } from "../../ws/encode";

const WS_URL = (import.meta.env.VITE_WS_URL as string | undefined) ?? "ws://127.0.0.1:9001";

export function AppShell(): React.JSX.Element {
  const { store, send } = useTelemetry(WS_URL);
  const [screen, setScreen] = useState<Screen>("overview");
  const [scalesOn, setScalesOn] = useState(true);
  const [paused, setPaused] = useState(false);

  const onPlayPause = () => {
    const next = !paused;
    setPaused(next);
    send(encodeCmd(next ? "pause" : "resume"));
  };

  const onSeek = (frac: number) => {
    send(encodeCmd("seek", Math.round(frac * store.durationMs())));
    setPaused(false);
  };

  const tsMs = store.lastTsMs();
  const clock = rideClock(tsMs);
  const status = deriveStatus(store);
  const rideTag = formatRideTag(tsMs);
  const channels = store.channels();
  const rateHz = store.rateHz() || 10;
  // buffered samples = longest strip series (robust to channel order / widget mix)
  const samples = channels.reduce((m, c) => Math.max(m, store.series(c.id)?.len() ?? 0), 0);
  const scrubberFrac = rideProgress(tsMs, store.durationMs());

  return (
    <div className="app-shell">
      <TopBar clock={clock} status={status} screen={screen} onScreen={setScreen}
        scalesOn={scalesOn} onToggleScales={() => setScalesOn((v) => !v)} />
      <div className="app-body">
        {screen === "overview" && <OverviewView store={store} />}
        {screen === "track" && <TrackView store={store} />}
        {screen === "events" && <EventsView store={store} />}
      </div>
      <TransportBar clock={clock} rideTag={rideTag} rateHz={rateHz}
        samples={samples} elapsedMs={tsMs} scrubberFrac={scrubberFrac}
        paused={paused} onPlayPause={onPlayPause} onSeek={onSeek} />
    </div>
  );
}
