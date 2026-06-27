import type React from "react";
import type { TelemetryStore } from "../../../data/store";
export function OverviewView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return <div data-testid="view-overview" className="view-stub">OVERVIEW — {store.channels().length} channels</div>;
}
