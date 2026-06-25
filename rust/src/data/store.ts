import type { ChannelMeta, EnumValue, MetaMessage, FrameMessage, MetricsMessage } from "../types";
import { ChannelSeries } from "./ringBuffer";
import { buildEnumIndex } from "../format/value";

export class TelemetryStore {
  private _channels: ChannelMeta[] = [];
  private _enumIndex = new Map<string, EnumValue>();
  private _latest: number[] = [];
  private _series = new Map<number, ChannelSeries>();
  private _latIdx = -1;
  private _lonIdx = -1;
  private _lat: number[] = [];
  private _lon: number[] = [];
  private _lastEmit = 0;
  private _metrics: MetricsMessage | undefined;

  constructor(private readonly windowMs = 60_000) {}

  applyMeta(m: MetaMessage): void {
    this._channels = m.channels;
    this._enumIndex = buildEnumIndex(m.enum_values);
    this._series.clear();
    this._latest = [];
    this._lat = [];
    this._lon = [];
    this._latIdx = -1;
    this._lonIdx = -1;
    m.channels.forEach((ch, i) => {
      if (ch.widget === "strip") this._series.set(ch.id, new ChannelSeries(this.windowMs));
      if (ch.widget === "map_lat") this._latIdx = i;
      if (ch.widget === "map_lon") this._lonIdx = i;
    });
  }

  applyFrame(f: FrameMessage): void {
    this._latest = f.values;
    this._lastEmit = f.emit_unix_ms;
    this._channels.forEach((ch, i) => {
      const series = this._series.get(ch.id);
      if (series) series.push(f.ts_ms, f.values[i]);
    });
    if (this._latIdx >= 0 && this._lonIdx >= 0) {
      this._lat.push(f.values[this._latIdx]);
      this._lon.push(f.values[this._lonIdx]);
    }
  }

  applyMetrics(m: MetricsMessage): void {
    this._metrics = m;
  }

  latest(channelId: number): number | undefined {
    const i = this._channels.findIndex((c) => c.id === channelId);
    return i >= 0 ? this._latest[i] : undefined;
  }

  series(channelId: number): ChannelSeries | undefined {
    return this._series.get(channelId);
  }

  gpsTrack(): { lat: number[]; lon: number[] } {
    return { lat: this._lat, lon: this._lon };
  }

  channels(): ChannelMeta[] {
    return this._channels;
  }

  enumIndex(): Map<string, EnumValue> {
    return this._enumIndex;
  }

  lastEmitUnixMs(): number {
    return this._lastEmit;
  }

  metrics(): MetricsMessage | undefined {
    return this._metrics;
  }
}
