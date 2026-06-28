import type React from "react";
import { useRef, useEffect } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { fmtNum } from "./gaugeViz";
import { toUPlotData, scrollWindow, fmtElapsedMs } from "./uplotData";
import { lineOpts } from "./uplotOpts";

const BASE_WINDOW_MS = 60_000; // 60s default scrolling window

function hasCanvas(): boolean {
  try { return !!document.createElement("canvas").getContext("2d"); } catch { return false; }
}

// Floating readout that follows the cursor: "m:ss · value unit" at the hovered sample.
function tooltipPlugin(unit: string): uPlot.Plugin {
  let tip: HTMLDivElement | null = null;
  return {
    hooks: {
      init: (u) => {
        tip = document.createElement("div");
        tip.className = "linechart-tip";
        tip.style.display = "none";
        u.over.appendChild(tip);
      },
      setCursor: (u) => {
        if (tip == null) return;
        const idx = u.cursor.idx;
        const left = u.cursor.left ?? -1;
        const top = u.cursor.top ?? 0;
        if (idx == null || left < 0) { tip.style.display = "none"; return; }
        const xv = u.data[0][idx];
        const yv = u.data[1][idx];
        if (xv == null || yv == null) { tip.style.display = "none"; return; }
        tip.textContent = `${fmtElapsedMs(xv)} · ${fmtNum(yv)} ${unit}`;
        tip.style.display = "block";
        tip.style.left = `${left}px`;
        tip.style.top = `${top}px`;
      },
    },
  };
}

export function LineChart({ name, xs, ys, unit, value, scalesOn, zoom }: {
  name: string; xs: number[]; ys: number[]; unit: string; value: number;
  scalesOn: boolean; zoom?: number;
}): React.JSX.Element {
  const elRef = useRef<HTMLDivElement>(null);
  const uRef = useRef<uPlot | null>(null);
  const windowMs = BASE_WINDOW_MS / Math.max(1, zoom ?? 1);

  // Mount uPlot (guarded off in jsdom). JS-size to the nearest real-height ancestor.
  useEffect(() => {
    const el = elRef.current;
    if (!el || !hasCanvas()) return;
    const fit = (): { w: number; h: number } => {
      let h = 0, w = el.clientWidth;
      let node: HTMLElement | null = el;
      while (node && h === 0) { h = node.clientHeight; if (h === 0) node = node.parentElement; }
      if (node) w = node.clientWidth || w;
      return { w: Math.max(50, w), h: Math.max(40, h) };
    };
    const { w, h } = fit();
    const u = new uPlot({ ...lineOpts(w, h), plugins: [tooltipPlugin(unit)] }, toUPlotData(xs, ys), el);
    u.setScale("x", { min: scrollWindow(xs, windowMs)[0], max: scrollWindow(xs, windowMs)[1] });
    uRef.current = u;
    const ro = new ResizeObserver(() => { const s = fit(); u.setSize({ width: s.w, height: s.h }); });
    ro.observe(el);
    return () => { ro.disconnect(); u.destroy(); uRef.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Stream data + scroll the window. xs.length is the live trigger (arrays mutate in place).
  useEffect(() => {
    const u = uRef.current; if (!u) return;
    u.setData(toUPlotData(xs, ys));
    const [min, max] = scrollWindow(xs, windowMs);
    u.setScale("x", { min, max });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [xs.length, windowMs]);

  return (
    <div data-testid="linechart" className="linechart-container">
      <div className="linechart-head">
        <span className="linechart-name">{name}</span>
        <span className="linechart-value">{fmtNum(value)}<span className="linechart-unit"> {unit}</span></span>
      </div>
      <div ref={elRef} className={`linechart-uplot${scalesOn ? "" : " linechart-noaxes"}`} />
    </div>
  );
}
