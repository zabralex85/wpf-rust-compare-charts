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
import { formatClock, formatRideTag } from "./clock";
import { deriveStatus } from "./status";
import type { Screen } from "./tabs";

const WS_URL = (import.meta.env.VITE_WS_URL as string | undefined) ?? "ws://127.0.0.1:9001";

export function AppShell(): React.JSX.Element {
  const { store } = useTelemetry(WS_URL);
  const [screen, setScreen] = useState<Screen>("overview");
  const [scalesOn, setScalesOn] = useState(true);

  const now = Date.now();
  const clock = formatClock(now);
  const status = deriveStatus(store);
  const rideTag = formatRideTag(0); // ride ts wiring refined in a later phase
  const channels = store.channels();
  const rateHz = 10; // TODO(later phase): source from meta.rate_hz when exposed
  // buffered samples = longest strip series (robust to channel order / widget mix)
  const samples = channels.reduce((m, c) => Math.max(m, store.series(c.id)?.len() ?? 0), 0);

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
        samples={samples} elapsedMs={(samples * 1000) / rateHz} scrubberFrac={0.88} />
    </div>
  );
}
