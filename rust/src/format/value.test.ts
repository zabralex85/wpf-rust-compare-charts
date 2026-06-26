import { describe, it, expect } from "vitest";
import { buildEnumIndex, decodeEnum, formatValue, severityColor } from "./value";
import type { ChannelMeta, EnumValue } from "../types";

const enums: EnumValue[] = [
  { channel_id: 15, code: 0, label: "Normal", severity: "ok" },
  { channel_id: 15, code: 1, label: "Critical", severity: "critical" },
];
const idx = buildEnumIndex(enums);

function ch(partial: Partial<ChannelMeta>): ChannelMeta {
  return { id: 1, name: "x", column_name: "x", unit: "", type: "real", min: 0, max: 1, widget: "table", display_order: 1, addr: "", ...partial };
}

describe("value formatting", () => {
  it("decodes enum codes", () => {
    expect(decodeEnum(15, 1, idx)?.label).toBe("Critical");
    expect(decodeEnum(15, 9, idx)).toBeUndefined();
  });

  it("formats enum channel as label", () => {
    expect(formatValue(ch({ id: 15, type: "enum" }), 1, idx)).toBe("Critical");
  });

  it("formats real channel to 3 decimals", () => {
    expect(formatValue(ch({ type: "real" }), 1.23456, idx)).toBe("1.235");
  });

  it("maps severity to color", () => {
    expect(severityColor("critical")).toBe("#d22");
    expect(severityColor("ok")).toBe("#2a2");
    expect(severityColor(undefined)).toBe("#888");
  });
});
