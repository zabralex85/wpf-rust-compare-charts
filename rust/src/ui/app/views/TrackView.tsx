import type React from "react";
import type { TelemetryStore } from "../../../data/store";
export function TrackView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return <div data-testid="view-track" className="view-stub">FLIGHT TRACK — {store.gpsTrack().lat.length} pts</div>;
}
