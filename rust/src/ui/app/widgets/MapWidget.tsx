import type React from "react";
import { useId, useState, useEffect, useRef } from "react";
import maplibregl from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import { mapStyle } from "./mapStyle";
import { trackToGeoJSON } from "./trackGeo";
import { projectTrack } from "./mapProj";
import { fmtCoord } from "./geoFormat";

interface MapWidgetProps {
  lat: number[];
  lon: number[];
}

// Default projection view matches mapProj defaults
const VIEW = { x: 60, y: 150, w: 470, h: 340 } as const;

// Centre of the projection view — anchor for range rings and axis lines
const CX = VIEW.x + VIEW.w / 2; // 295
const CY = VIEW.y + VIEW.h / 2; // 320

// Axis tick length (px in SVG units)
const TICK = 8;

/** Local tile server base URL — matches the Rust default RIDE_TILES_PORT=9002 */
const TILES_BASE = "http://127.0.0.1:9002";

type PointFeature = {
  type: "Feature";
  properties: Record<string, never>;
  geometry: { type: "Point"; coordinates: [number, number] };
};

type EmptyCollection = {
  type: "FeatureCollection";
  features: [];
};

/** GeoJSON Point at the last track position, or empty FeatureCollection when no data. */
function posPoint(lat: number[], lon: number[]): PointFeature | EmptyCollection {
  if (lat.length === 0 || lon.length === 0) {
    return { type: "FeatureCollection", features: [] };
  }
  const i = lat.length - 1;
  return {
    type: "Feature",
    properties: {},
    geometry: { type: "Point", coordinates: [lon[i], lat[i]] },
  };
}

export function MapWidget({ lat, lon }: MapWidgetProps): React.JSX.Element {
  const gridId = useId();
  const { path, last } = projectTrack(lat, lon);
  const [osm, setOsm] = useState(false);
  const elRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<maplibregl.Map | null>(null);

  // Mount/teardown MapLibre only while OSM is on AND the container has real size
  // (jsdom reports 0 → unit tests never instantiate a real map).
  useEffect(() => {
    if (!osm) return;
    const el = elRef.current;
    if (!el || el.clientWidth === 0 || el.clientHeight === 0) return;
    const map = new maplibregl.Map({
      container: el,
      style: mapStyle(TILES_BASE),
      center: [34.7818, 32.0853], // lon,lat (Tel Aviv)
      zoom: 9,
      // attributionControl defaults to showing attribution in MapLibre v5
    });
    map.on("load", () => {
      map.addSource("track", { type: "geojson", data: trackToGeoJSON(lat, lon) });
      map.addLayer({
        id: "track-line",
        type: "line",
        source: "track",
        paint: { "line-color": "#38c5e0", "line-width": 2.5 },
      });
      // live-position marker: a point source + circle layer at the last point
      map.addSource("pos", { type: "geojson", data: posPoint(lat, lon) });
      map.addLayer({
        id: "pos-dot",
        type: "circle",
        source: "pos",
        paint: {
          "circle-radius": 5,
          "circle-color": "#2fd17a",
          "circle-stroke-color": "#0a0e14",
          "circle-stroke-width": 2,
        },
      });
    });
    mapRef.current = map;
    return () => {
      map.remove();
      mapRef.current = null;
    };
  }, [osm]);

  // Update the track + position sources as the track grows
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const ts = map.getSource("track") as maplibregl.GeoJSONSource | undefined;
    const ps = map.getSource("pos") as maplibregl.GeoJSONSource | undefined;
    if (ts) ts.setData(trackToGeoJSON(lat, lon));
    if (ps) ps.setData(posPoint(lat, lon));
  }, [lat, lon, osm]);

  return (
    <div data-testid="mapwidget" className="mapwidget-container">
      <button className="mapwidget-osm-toggle" onClick={() => setOsm((v) => !v)}>
        {osm ? "GRID VIEW" : "OSM MAP"}
      </button>
      {osm && <div ref={elRef} className="mapwidget-osm" />}
      {/*
       * ViewBox: "55 145 480 350"
       * Covers (55,145)→(535,495), giving a 5 px margin around the
       * default projection view corners at (60,150) and (530,490).
       */}
      <svg
        viewBox="55 145 480 350"
        preserveAspectRatio="xMidYMid meet"
        className="mapwidget-svg"
      >
        <defs>
          {/* Grid pattern — unique id prevents conflicts when multiple MapWidgets coexist */}
          <pattern
            id={gridId}
            width="40"
            height="40"
            patternUnits="userSpaceOnUse"
          >
            <path
              d="M40 0V40M0 40H40"
              fill="none"
              className="mapwidget-grid-path"
            />
          </pattern>
        </defs>

        {/* Grid fill */}
        <rect
          x={VIEW.x}
          y={VIEW.y}
          width={VIEW.w}
          height={VIEW.h}
          fill={`url(#${gridId})`}
          className="mapwidget-grid-rect"
        />

        {/* Faint cross-hair axis lines through the view centre */}
        <line
          x1={CX}
          y1={VIEW.y}
          x2={CX}
          y2={VIEW.y + VIEW.h}
          className="mapwidget-axis"
        />
        <line
          x1={VIEW.x}
          y1={CY}
          x2={VIEW.x + VIEW.w}
          y2={CY}
          className="mapwidget-axis"
        />

        {/* Decorative range rings — concentric, centred on view centre */}
        <circle cx={CX} cy={CY} r={50} className="mapwidget-ring" />
        <circle cx={CX} cy={CY} r={100} className="mapwidget-ring" />
        <circle
          cx={CX}
          cy={CY}
          r={150}
          className="mapwidget-ring mapwidget-ring-outer"
        />

        {/* Cardinal axis ticks — short strokes at N / S / E / W edges */}
        {/* N */}
        <line
          x1={CX}
          y1={VIEW.y}
          x2={CX}
          y2={VIEW.y + TICK}
          className="mapwidget-tick"
        />
        {/* S */}
        <line
          x1={CX}
          y1={VIEW.y + VIEW.h}
          x2={CX}
          y2={VIEW.y + VIEW.h - TICK}
          className="mapwidget-tick"
        />
        {/* E */}
        <line
          x1={VIEW.x + VIEW.w}
          y1={CY}
          x2={VIEW.x + VIEW.w - TICK}
          y2={CY}
          className="mapwidget-tick"
        />
        {/* W */}
        <line
          x1={VIEW.x}
          y1={CY}
          x2={VIEW.x + TICK}
          y2={CY}
          className="mapwidget-tick"
        />

        {/* Flight track — path is "" when no data, which renders nothing */}
        <path
          d={path}
          fill="none"
          className="mapwidget-track"
          vectorEffect="non-scaling-stroke"
        />

        {/* Live-position marker — circle at the last projected point */}
        {last !== null && (
          <circle
            cx={last.x}
            cy={last.y}
            r={4}
            className="mapwidget-marker"
          />
        )}
      </svg>
      {/* geographic chrome overlays */}
      <div className="mapwidget-compass">N↑</div>
      <div className="mapwidget-coords">{fmtCoord(lat, lon)}</div>
      <div className="mapwidget-scale"><span className="mapwidget-scale-bar" />2 km</div>
    </div>
  );
}
