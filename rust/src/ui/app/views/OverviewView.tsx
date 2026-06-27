import type React from "react";
import type { TelemetryStore } from "../../../data/store";
import { ParamPanel } from "../ParamPanel";

export function OverviewView({ store }: { store: TelemetryStore }): React.JSX.Element {
  return (
    <div data-testid="view-overview" className="overview">
      <div className="overview-left"><ParamPanel store={store} /></div>
      <div data-testid="overview-dash" className="overview-dash">DASHBOARD — widgets in Phase 3</div>
    </div>
  );
}
