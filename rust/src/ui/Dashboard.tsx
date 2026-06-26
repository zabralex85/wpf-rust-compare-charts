import type React from "react";
import { useTelemetry } from "./useTelemetry";
import { ParamTable } from "./ParamTable";
import { StripChart, type StripLine } from "./StripChart";
import { Gauge } from "./Gauge";
import { GpsMap } from "./GpsMap";
import { Hud } from "./Hud";
import "./Dashboard.css";

const WS_URL = (import.meta.env.VITE_WS_URL as string | undefined) ?? "ws://127.0.0.1:9001";
const STROKES = ["#e33", "#fff", "#3cf", "#fc3", "#3f6", "#f6f", "#fa0", "#0fa"];

export function Dashboard(): React.JSX.Element {
  const { store, fps, frameTimeMs, status } = useTelemetry(WS_URL);
  const chans = store.channels();
  const stripChans = chans.filter((c) => c.widget === "strip");
  const accZ = stripChans.find((c) => c.column_name === "acc_z");
  const mainChans = stripChans.filter((c) => c !== accZ);
  const gaugeChans = chans.filter((c) => c.widget === "gauge");
  const gps = store.gpsTrack();

  const toLines = (ids: typeof stripChans): StripLine[] =>
    ids.map((c, i) => {
      const s = store.series(c.id);
      const a = s ? s.arrays() : { xs: [], ys: [] };
      return { label: c.name, stroke: STROKES[i % STROKES.length], xs: a.xs.map((x) => x / 1000), ys: a.ys };
    });

  return (
    <div className="dashboard">
      <aside className="left"><ParamTable store={store} /></aside>
      <main className="center">
        <StripChart title="Main" lines={toLines(mainChans)} width={640} height={320} />
        {accZ && <StripChart title={accZ.name} lines={toLines([accZ])} width={640} height={200} />}
      </main>
      <aside className="right">
        <GpsMap lat={gps.lat} lon={gps.lon} />
        <div className="gauges">
          {gaugeChans.map((c) => (
            <Gauge key={c.id} label={c.name} unit={c.unit} value={store.latest(c.id) ?? c.min} min={c.min} max={c.max} />
          ))}
        </div>
      </aside>
      <Hud store={store} fps={fps} frameTimeMs={frameTimeMs} nowMs={Date.now()} />
      <div className={`status status-${status}`}>ws: {status}</div>
    </div>
  );
}
