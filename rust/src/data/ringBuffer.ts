export class ChannelSeries {
  private xs: number[] = [];
  private ys: number[] = [];
  constructor(private readonly windowMs: number) {}

  push(tsMs: number, value: number): void {
    this.xs.push(tsMs);
    this.ys.push(value);
    const cutoff = tsMs - this.windowMs;
    let drop = 0;
    while (drop < this.xs.length && this.xs[drop] < cutoff) drop++;
    if (drop > 0) {
      this.xs.splice(0, drop);
      this.ys.splice(0, drop);
    }
  }

  arrays(): { xs: number[]; ys: number[] } {
    return { xs: this.xs, ys: this.ys };
  }

  len(): number {
    return this.xs.length;
  }
}
