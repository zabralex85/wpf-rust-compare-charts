import type React from "react";
import type { TelemetryStore } from "../../../data/store";
import type { ChannelMeta } from "../../../types";
import { defaultWidgets } from "./layout";
import { Gauge } from "./Gauge";
import { LineChart } from "./LineChart";
import { MapWidget } from "./MapWidget";

interface WidgetGridProps {
  store: TelemetryStore;
  scalesOn: boolean;
}

export function WidgetGrid({ store, scalesOn }: WidgetGridProps): React.JSX.Element {
  const channels = store.channels();
  const widgets = defaultWidgets(channels);

  // Build id → ChannelMeta lookup once per render
  const chMap = new Map<number, ChannelMeta>();
  for (const ch of channels) {
    chMap.set(ch.id, ch);
  }

  return (
    <div className="widgetgrid">
      {widgets.map((w) => {
        const chUnit = w.channelId !== undefined ? (chMap.get(w.channelId)?.unit ?? "") : "";

        let inner: React.JSX.Element;

        if (w.kind === "gauge") {
          inner = (
            <Gauge
              name={w.name}
              value={store.latest(w.channelId!) ?? 0}
              unit={chUnit}
              scalesOn={scalesOn}
            />
          );
        } else if (w.kind === "line") {
          const { xs, ys } = store.series(w.channelId!)?.arrays() ?? { xs: [], ys: [] };
          inner = (
            <LineChart
              name={w.name}
              xs={xs}
              ys={ys}
              unit={chUnit}
              value={store.latest(w.channelId!) ?? 0}
              scalesOn={scalesOn}
            />
          );
        } else {
          const { lat, lon } = store.gpsTrack();
          inner = <MapWidget lat={lat} lon={lon} />;
        }

        return (
          <div
            key={w.id}
            className="widget-cell"
            style={{
              gridColumn: `span ${w.cols}`,
              gridRow: `span ${w.rows}`,
            }}
          >
            <div className="widget-cell-header">{w.name}</div>
            <div className="widget-cell-body">{inner}</div>
          </div>
        );
      })}
    </div>
  );
}
