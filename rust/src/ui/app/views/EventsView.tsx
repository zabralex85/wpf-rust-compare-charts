import type React from "react";
import type { TelemetryStore } from "../../../data/store";
export function EventsView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return <div data-testid="view-events" className="view-stub">EVENTS — {store.metrics() ? "metrics" : "no metrics"}</div>;
}
