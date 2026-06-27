import type React from "react";
import type { TelemetryStore } from "../../data/store";
import type { DragPayload } from "./widgets/widgetModel";
import { groupChannels } from "./groups";
import { paramRow } from "./paramrow";

export function ParamPanel({ store }: { store: TelemetryStore }): React.JSX.Element {
  const groups = groupChannels(store.channels());
  return (
    <div className="param-panel">
      <div className="param-hd">
        <div className="param-hd-title"><span className="param-hd-dot" />PARAMETERS</div>
        <div className="param-hd-meta"><span className="param-all">ALL</span><span className="param-ch">{store.channels().length} CH</span></div>
      </div>
      <div className="param-colhd"><span /><span>PARAMETER</span><span className="r">ENG·DATA</span><span /><span className="r">BUS</span></div>
      <div className="param-rows">
        {groups.map((g) => (
          <div key={g.group}>
            <div className="param-grp"><span>{g.group}</span><span>{g.channels.length}</span></div>
            {g.channels.map((ch) => {
              const r = paramRow(ch, store);
              return (
                <div
                  key={ch.id}
                  className={`param-row${r.critical ? " param-row-crit" : ""}`}
                  data-prow={ch.id}
                  draggable
                  onDragStart={(e) => {
                    const payload: DragPayload = { channelId: ch.id, name: ch.name, unit: ch.unit };
                    try {
                      e.dataTransfer.effectAllowed = "copy";
                      e.dataTransfer.setData("application/x-inu-param", JSON.stringify(payload));
                      e.dataTransfer.setData("text/plain", JSON.stringify(payload));
                    } catch {
                      /* jsdom may lack dataTransfer; ignored */
                    }
                  }}
                >
                  <span className="param-dot" style={{ background: r.dotColor }} />
                  <span className="param-name">{ch.name}</span>
                  <span className="param-val r" style={{ color: r.valueColor }}>{r.text}</span>
                  <span className="param-unit">{ch.unit}</span>
                  <span className="param-bus r">{ch.addr}</span>
                </div>
              );
            })}
          </div>
        ))}
      </div>
    </div>
  );
}
