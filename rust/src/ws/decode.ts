import type { WsMessage } from "../types";

export function decodeMessage(json: string): WsMessage {
  const obj = JSON.parse(json) as { type?: unknown };
  switch (obj.type) {
    case "meta":
    case "frame":
    case "metrics":
      return obj as WsMessage;
    default:
      throw new Error(`unknown message type: ${String(obj.type)}`);
  }
}
