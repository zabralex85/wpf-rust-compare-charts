import type uPlot from "uplot";

export function lineOpts(width: number, height: number): uPlot.Options {
  return {
    width,
    height,
    scales: {
      x: { time: true },
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
        // x axis
        stroke: "#566273",
        grid: { stroke: "#1d2632", width: 1 },
        ticks: { stroke: "#1d2632" },
        font: "10px 'IBM Plex Mono'",
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
