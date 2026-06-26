export function gaugeAngle(value: number, min: number, max: number, startDeg: number, endDeg: number): number {
  if (max <= min) return startDeg;
  const clamped = Math.min(max, Math.max(min, value));
  const t = (clamped - min) / (max - min);
  return startDeg + t * (endDeg - startDeg);
}
