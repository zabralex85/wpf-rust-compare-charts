export class FpsMeter {
  private ts: number[] = [];
  constructor(private readonly windowSize = 60) {}

  tick(tsMs: number): void {
    this.ts.push(tsMs);
    if (this.ts.length > this.windowSize) this.ts.shift();
  }

  fps(): number {
    if (this.ts.length < 2) return 0;
    const span = this.ts[this.ts.length - 1] - this.ts[0];
    if (span <= 0) return 0;
    return ((this.ts.length - 1) * 1000) / span;
  }

  frameTimeMs(): number {
    if (this.ts.length < 2) return 0;
    const span = this.ts[this.ts.length - 1] - this.ts[0];
    return span / (this.ts.length - 1);
  }
}

export function latencyMs(emitUnixMs: number, nowMs: number): number {
  return nowMs - emitUnixMs;
}
