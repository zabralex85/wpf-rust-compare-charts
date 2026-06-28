import type React from "react";
import type { TelemetryStore } from "../../../data/store";
import { ParamPanel } from "../ParamPanel";
import { WidgetGrid } from "../widgets/WidgetGrid";

export function OverviewView({ store }: { store: TelemetryStore }): React.JSX.Element {
  const hasChannels = store.channels().length > 0;
  return (
    <div data-testid="view-overview" className="overview">
      <div className="overview-left"><ParamPanel store={store} /></div>
      <div data-testid="overview-dash" className="overview-dash">
        {hasChannels
          ? <WidgetGrid store={store} scalesOn={true} />
          : <div className="overview-dash-waiting">Waiting for telemetry…</div>}
      </div>
    </div>
  );
}
