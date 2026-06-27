const PITCH = 168;

export function cellFromPoint(
  rect: { left: number; top: number },
  clientX: number,
  clientY: number,
  scrollLeft: number,
  scrollTop: number,
  pitch: number = PITCH,
): { col: number; row: number } {
  const col = Math.max(1, Math.floor((clientX - rect.left + scrollLeft) / pitch) + 1);
  const row = Math.max(1, Math.floor((clientY - rect.top + scrollTop) / pitch) + 1);
  return { col, row };
}

export function resizeStep(delta: number, pitch: number = PITCH): number {
  const dead = pitch - 24; // 144 at pitch 168 (sample's deadzone)
  if (Math.abs(delta) < dead) return 0;
  if (delta > 0) return Math.floor((delta - dead) / pitch) + 1;
  return Math.ceil((delta + dead) / pitch) - 1;
}
