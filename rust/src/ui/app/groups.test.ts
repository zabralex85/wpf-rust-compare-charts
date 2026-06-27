import { describe, it, expect } from "vitest";
import { groupOf, groupChannels } from "./groups";
import type { ChannelMeta } from "../../types";

function ch(col: string, order: number): ChannelMeta {
  return { id: order, name: col, column_name: col, unit: "", type: "real", min: 0, max: 1, widget: "table", display_order: order, addr: "I_01" };
}

describe("groupOf", () => {
  it("maps known columns and defaults to System", () => {
    expect(groupOf("roll")).toBe("Attitude");
    expect(groupOf("vel_x")).toBe("Velocity");
    expect(groupOf("inu_mode2")).toBe("INU Mode");
    expect(groupOf("temp")).toBe("System");
    expect(groupOf("wat")).toBe("System");
  });
});

describe("groupChannels", () => {
  it("buckets in group order, omits empties, sorts by display_order", () => {
    const chs = [ch("temp", 3), ch("roll", 2), ch("pitch", 1), ch("vel_x", 4)];
    const g = groupChannels(chs);
    expect(g.map((x) => x.group)).toEqual(["Velocity", "Attitude", "System"]);
    const att = g.find((x) => x.group === "Attitude")!;
    expect(att.channels.map((c) => c.column_name)).toEqual(["pitch", "roll"]);
  });
});
