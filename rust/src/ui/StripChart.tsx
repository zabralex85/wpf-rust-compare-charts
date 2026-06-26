import type React from "react";
import { useEffect, useRef } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";

export interface StripLine { label: string; stroke: string; xs: number[]; ys: number[]; }

export function StripChart({ title, lines, width, height }: { title: string; lines: StripLine[]; width: number; height: number }): React.JSX.Element {
  const elRef = useRef<HTMLDivElement>(null);
  const uRef = useRef<uPlot | null>(null);

  useEffect(() => {
    if (!elRef.current) return;
    const opts: uPlot.Options = {
      title,
      width,
      height,
      scales: { x: { time: false } },
      series: [
        {},
        ...lines.map((l) => ({ label: l.label, stroke: l.stroke, points: { show: false } })),
      ],
    };
    const data: uPlot.AlignedData = [lines[0]?.xs ?? [], ...lines.map((l) => l.ys)];
    const u = new uPlot(opts, data, elRef.current);
    uRef.current = u;
    return () => { u.destroy(); uRef.current = null; };
    // recreate only when structure (title/series count/size) changes
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [title, width, height, lines.length]);

  useEffect(() => {
    const u = uRef.current;
    if (!u) return;
    const data: uPlot.AlignedData = [lines[0]?.xs ?? [], ...lines.map((l) => l.ys)];
    u.setData(data);
  });

  return <div ref={elRef} className="strip-chart" />;
}
