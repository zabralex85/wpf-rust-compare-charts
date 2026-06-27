function p2(n: number): string { return n.toString().padStart(2, "0"); }
function p3(n: number): string { return n.toString().padStart(3, "0"); }

export function formatClock(unixMs: number): { hms: string; ms: string } {
  const d = new Date(unixMs);
  return {
    hms: `${p2(d.getUTCHours())}:${p2(d.getUTCMinutes())}:${p2(d.getUTCSeconds())}`,
    ms: p3(d.getUTCMilliseconds()),
  };
}

export function formatRideTag(tsMs: number): string {
  const totalSec = Math.floor(tsMs / 1000);
  const mm = Math.floor(totalSec / 60);
  const ss = totalSec % 60;
  const ms = tsMs % 1000;
  return `${p2(mm)}:${p2(ss)}.${p3(ms)}`;
}
