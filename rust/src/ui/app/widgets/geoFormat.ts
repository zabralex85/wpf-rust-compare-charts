export function fmtCoord(lat: number[], lon: number[]): string {
  if (lat.length === 0 || lon.length === 0) return "—";
  const la = lat[lat.length - 1];
  const lo = lon[lon.length - 1];
  const ns = la >= 0 ? "N" : "S";
  const ew = lo >= 0 ? "E" : "W";
  return `${Math.abs(la).toFixed(4)}°${ns} ${Math.abs(lo).toFixed(4)}°${ew}`;
}
