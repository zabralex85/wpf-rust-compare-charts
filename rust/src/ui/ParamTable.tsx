import React from "react";
import type { TelemetryStore } from "../data/store";
import { formatValue, severityColor, decodeEnum } from "../format/value";

export function ParamTable({ store }: { store: TelemetryStore }): React.JSX.Element {
  const idx = store.enumIndex();
  return (
    <table className="param-table">
      <thead>
        <tr><th>Parameter</th><th>Eng. Data</th><th>Bar</th><th>Addr</th></tr>
      </thead>
      <tbody>
        {store.channels().map((ch) => {
          const v = store.latest(ch.id);
          const text = v === undefined ? "" : formatValue(ch, v, idx);
          const frac = v === undefined || ch.max <= ch.min ? 0
            : Math.min(1, Math.max(0, (v - ch.min) / (ch.max - ch.min)));
          const color = ch.type === "enum" && v !== undefined
            ? severityColor(decodeEnum(ch.id, v, idx)?.severity)
            : "#2a7";
          return (
            <tr key={ch.id}>
              <td className="name">{ch.name}</td>
              <td className="value">{text}</td>
              <td className="bar"><div className="bar-fill" style={{ width: `${frac * 100}%`, background: color }} /></td>
              <td className="addr">{ch.addr}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
