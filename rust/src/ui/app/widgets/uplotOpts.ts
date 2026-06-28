import type uPlot from "uplot";
import { fmtElapsed } from "./uplotData";

export function lineOpts(width: number, height: number): uPlot.Options {
  return {
    width,
    height,
    scales: {
      // x is elapsed seconds from ride start, not a wall-clock time.
      x: { time: false },
      y: { auto: true },
    },
    series: [
      {
        // x (time axis): no stroke
      },
      {
        // value line
        stroke: "#38c5e0",
        width: 1.4,
        points: { show: false },
      },
    ],
    axes: [
      {
        // x axis — relative elapsed time (m:ss)
        stroke: "#566273",
        grid: { stroke: "#1d2632", width: 1 },
        ticks: { stroke: "#1d2632" },
        font: "10px 'IBM Plex Mono'",
        values: (_u, splits) => splits.map((v) => fmtElapsed(v)),
      },
      {
        // y axis
        stroke: "#566273",
        grid: { stroke: "#1d2632", width: 1 },
        ticks: { stroke: "#1d2632" },
        font: "10px 'IBM Plex Mono'",
      },
    ],
    legend: {
      show: false,
    },
    cursor: {
      show: true,
    },
  };
}
