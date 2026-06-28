import type React from "react";
import { useId, useState, useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
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

export function MapWidget({ lat, lon }: MapWidgetProps): React.JSX.Element {
  const gridId = useId();
  const { path, last } = projectTrack(lat, lon);
  const [osm, setOsm] = useState(false);
  const elRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const lineRef = useRef<L.Polyline | null>(null);
  const markerRef = useRef<L.CircleMarker | null>(null);

  // Mount/teardown Leaflet only while OSM is on AND the container has real size
  // (jsdom reports 0 → unit tests never instantiate a real map).
  useEffect(() => {
    if (!osm) return;
    const el = elRef.current;
    if (!el || el.clientWidth === 0 || el.clientHeight === 0) return;
    const map = L.map(el, { zoomControl: true, attributionControl: true }).setView([32.0853, 34.7818], 11);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "© OpenStreetMap contributors",
      maxZoom: 19,
    }).addTo(map);
    lineRef.current = L.polyline([], { color: "#38c5e0", weight: 2.5, opacity: 0.95 }).addTo(map);
    markerRef.current = L.circleMarker([32.0853, 34.7818], {
      radius: 6,
      color: "#0a0e14",
      weight: 2,
      fillColor: "#ffffff",
      fillOpacity: 1,
    }).addTo(map);
    mapRef.current = map;
    const sizeTimer = setTimeout(() => map.invalidateSize(), 60);
    return () => {
      clearTimeout(sizeTimer);
      map.remove();
      mapRef.current = null;
      lineRef.current = null;
      markerRef.current = null;
    };
  }, [osm]);

  // Update the polyline + marker as the track grows
  useEffect(() => {
    const line = lineRef.current;
    const marker = markerRef.current;
    const map = mapRef.current;
    if (!line || !marker || !map || lat.length === 0) return;
    const pts: [number, number][] = lat.map((la, i) => [la, lon[i]]);
    line.setLatLngs(pts);
    const lastPt = pts[pts.length - 1];
    marker.setLatLng(lastPt);
    map.panTo(lastPt, { animate: false });
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
