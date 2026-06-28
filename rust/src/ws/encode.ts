export type CmdAction = "pause" | "resume" | "seek";

export function encodeCmd(action: CmdAction, tsMs?: number): string {
  const cmd: { type: string; action: CmdAction; ts_ms?: number } = {
    type: "cmd",
    action,
  };
  // Only include ts_ms for seek actions
  if (action === "seek" && tsMs !== undefined) {
    cmd.ts_ms = tsMs;
  }
  return JSON.stringify(cmd);
}
