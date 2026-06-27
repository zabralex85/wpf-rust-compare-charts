function p2(n: number): string {
  return n.toString().padStart(2, "0");
}
function p3(n: number): string {
  return n.toString().padStart(3, "0");
}

export function rideClock(tsMs: number): { hms: string; ms: string } {
  const t = Math.max(0, Math.floor(tsMs));
  const totalSec = Math.floor(t / 1000);
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  return { hms: `${p2(h)}:${p2(m)}:${p2(s)}`, ms: p3(t % 1000) };
}

export function rideProgress(tsMs: number, durationMs: number): number {
  if (durationMs <= 0) return 0;
  return Math.min(1, Math.max(0, tsMs / durationMs));
}
