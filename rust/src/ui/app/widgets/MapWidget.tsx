import type React from "react";
import { useEffect, useRef } from "react";
import maplibregl from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import { mapStyle } from "./mapStyle";
import { trackToGeoJSON } from "./trackGeo";
import { fmtCoord } from "./geoFormat";

interface MapWidgetProps {
  lat: number[];
  lon: number[];
}

/** Local tile server base URL — matches the Rust default RIDE_TILES_PORT=9002 */
const TILES_BASE = "http://127.0.0.1:9002";

/** Fit the map to the flight track's bounding box (with padding); no-op when empty.
 * Without this the track can be a sub-pixel dot at the fixed initial zoom. */
function fitTrack(map: maplibregl.Map, lat: number[], lon: number[]): void {
  const n = Math.min(lat.length, lon.length);
  if (n === 0) return;
  let minLa = Infinity, maxLa = -Infinity, minLo = Infinity, maxLo = -Infinity;
  for (let i = 0; i < n; i++) {
    if (lat[i] < minLa) minLa = lat[i];
    if (lat[i] > maxLa) maxLa = lat[i];
    if (lon[i] < minLo) minLo = lon[i];
    if (lon[i] > maxLo) maxLo = lon[i];
  }
  map.fitBounds(
    [[minLo, minLa], [maxLo, maxLa]],
    { padding: 40, maxZoom: 15, duration: 0 },
  );
}

/** WebGL available? jsdom (unit tests) returns false → MapLibre is never constructed there.
 * A real browser / WebView2 returns true even if the container is momentarily 0-size. */
function hasWebGL(): boolean {
  try {
    const c = document.createElement("canvas");
    return !!(c.getContext("webgl2") || c.getContext("webgl"));
  } catch {
    return false;
  }
}

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

// The map renders the offline MVT basemap (MapLibre GL) directly — 1:1 with the .NET
// Skia basemap. There is no decorative "grid view" and no OSM/GRID toggle: when a tileset
// or WebGL is unavailable the container just shows its dark background (matching .NET's
// no-tileset behaviour).
export function MapWidget({ lat, lon }: MapWidgetProps): React.JSX.Element {
  const elRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<maplibregl.Map | null>(null);

  // Mount/teardown MapLibre. Guard on WebGL availability (not container size): jsdom has no
  // WebGL so unit tests never construct a map, but a real WebView mounts even if the overlay
  // is momentarily 0-size (a ResizeObserver + resize() below paints it once layout settles).
  useEffect(() => {
    const el = elRef.current;
    if (!el || !hasWebGL()) return;
    let cancelled = false;
    const map = new maplibregl.Map({
      container: el,
      style: mapStyle(TILES_BASE),
      center: [34.7818, 32.0853], // lon,lat (Tel Aviv)
      zoom: 9,
      // attributionControl defaults to showing attribution in MapLibre v5
    });
    // WebView2 collapses the %-height chain → the overlay reports height 0.
    // Force a concrete pixel size from the nearest non-zero-height ancestor.
    const fit = (): void => {
      let h = 0;
      let node: HTMLElement | null = el.parentElement;
      while (node && h === 0) {
        h = node.clientHeight;
        node = node.parentElement;
      }
      if (h > 0) {
        el.style.height = `${h}px`;
        el.style.width = `${el.parentElement?.clientWidth ?? el.clientWidth}px`;
      }
      map.resize();
    };
    fit();
    // Repaint when the overlay gets/changes size (it mounts at the cell's size,
    // but layout may not be flushed at construction time).
    const ro = new ResizeObserver(() => fit());
    ro.observe(el);
    map.on("load", () => {
      if (cancelled) return;
      fit();
      // late re-fits in case layout settled after load
      requestAnimationFrame(() => { if (!cancelled) fit(); });
      setTimeout(() => { if (!cancelled) fit(); }, 300);
      map.addSource("track", { type: "geojson", data: trackToGeoJSON(lat, lon) });
      map.addLayer({
        id: "track-line",
        type: "line",
        source: "track",
        layout: { "line-cap": "round", "line-join": "round" },
        paint: { "line-color": "#38c5e0", "line-width": 4 },
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
      // Zoom/centre to the flight track (it can span only a few hundred metres —
      // the fixed initial zoom would render it sub-pixel).
      fitTrack(map, lat, lon);
    });
    mapRef.current = map;
    return () => {
      cancelled = true;
      ro.disconnect();
      map.remove();
      mapRef.current = null;
    };
  }, []);

  // Update the track + position sources as the track grows
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const ts = map.getSource("track") as maplibregl.GeoJSONSource | undefined;
    const ps = map.getSource("pos") as maplibregl.GeoJSONSource | undefined;
    if (ts) ts.setData(trackToGeoJSON(lat, lon));
    if (ps) ps.setData(posPoint(lat, lon));
    // `lat.length` is the live trigger: the store mutates the SAME array in place,
    // so the array ref never changes — only the length grows as points arrive.
  }, [lat, lon, lat.length, lon.length]);

  return (
    <div data-testid="mapwidget" className="mapwidget-container">
      {/* MapLibre basemap canvas — dark background shows through when no tiles/WebGL. */}
      <div ref={elRef} className="mapwidget-osm" />
      {/* geographic chrome overlays */}
      <div className="mapwidget-compass">N↑</div>
      <div className="mapwidget-coords">{fmtCoord(lat, lon)}</div>
      <div className="mapwidget-scale"><span className="mapwidget-scale-bar" />2 km</div>
    </div>
  );
}
