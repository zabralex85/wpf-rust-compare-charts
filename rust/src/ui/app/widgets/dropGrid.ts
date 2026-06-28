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
  const dead = pitch - 24; // 144 at pitch 168 — sample's offset
  const result = delta >= 0 ? Math.floor((delta + dead) / pitch) : Math.ceil((delta - dead) / pitch);
  return result + 0; // normalize -0 to 0
}
