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
  private _idToIndex = new Map<number, number>();
  private _rateHz = 0;
  private _durationMs = 0;
  private _lastTs = 0;

  constructor(private readonly windowMs = 60_000) {}

  applyMeta(m: MetaMessage): void {
    // channels arrive pre-sorted by display_order; frame.values[i] aligns to channels[i] positionally.
    this._channels = m.channels;
    this._enumIndex = buildEnumIndex(m.enum_values);
    this._series.clear();
    this._latest = [];
    this._lat = [];
    this._lon = [];
    this._lastEmit = 0;
    this._metrics = undefined;
    this._latIdx = -1;
    this._lonIdx = -1;
    this._idToIndex.clear();
    this._rateHz = m.rate_hz;
    this._durationMs = m.duration_s * 1000;
    this._lastTs = 0;
    m.channels.forEach((ch, i) => {
      if (ch.widget === "strip") this._series.set(ch.id, new ChannelSeries(this.windowMs));
      if (ch.widget === "map_lat") this._latIdx = i;
      if (ch.widget === "map_lon") this._lonIdx = i;
      this._idToIndex.set(ch.id, i);
    });
  }

  applyFrame(f: FrameMessage): void {
    if (f.values.length !== this._channels.length) return;
    this._latest = f.values;
    this._lastEmit = f.emit_unix_ms;
    this._lastTs = f.ts_ms;
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
    const i = this._idToIndex.get(channelId);
    return i === undefined ? undefined : this._latest[i];
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

  lastTsMs(): number {
    return this._lastTs;
  }

  rateHz(): number {
    return this._rateHz;
  }

  durationMs(): number {
    return this._durationMs;
  }
}
