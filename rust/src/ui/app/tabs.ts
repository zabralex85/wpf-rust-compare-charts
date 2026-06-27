export type Screen = "overview" | "track" | "events";

export const SCREENS: { key: Screen; label: string }[] = [
  { key: "overview", label: "OVERVIEW" },
  { key: "track", label: "FLIGHT TRACK" },
  { key: "events", label: "EVENTS" },
];

export function formatCount(n: number): string {
  return n.toLocaleString("en-US");
}

export function formatElapsed(ms: number): string {
  const total = Math.floor(Math.max(0, ms) / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return `${h}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
}
