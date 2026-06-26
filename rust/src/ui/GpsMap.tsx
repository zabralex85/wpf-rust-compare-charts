// rust/src/ui/GpsMap.tsx
import type React from "react";
import { useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";

export function GpsMap({ lat, lon }: { lat: number[]; lon: number[] }): React.JSX.Element {
  const elRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<L.Map | null>(null);
  const lineRef = useRef<L.Polyline | null>(null);
  const markerRef = useRef<L.CircleMarker | null>(null);

  useEffect(() => {
    if (!elRef.current) return;
    const map = L.map(elRef.current, { zoomControl: false }).setView([32.08, 34.78], 11);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "© OpenStreetMap contributors",
      maxZoom: 19,
    }).addTo(map);
    lineRef.current = L.polyline([], { color: "#e33", weight: 2 }).addTo(map);
    markerRef.current = L.circleMarker([32.08, 34.78], { radius: 5, color: "#fff", fillColor: "#e33", fillOpacity: 1 }).addTo(map);
    mapRef.current = map;
    return () => { map.remove(); mapRef.current = null; };
  }, []);

  useEffect(() => {
    const line = lineRef.current, marker = markerRef.current, map = mapRef.current;
    if (!line || !marker || !map || lat.length === 0) return;
    const pts: [number, number][] = lat.map((la, i) => [la, lon[i]]);
    line.setLatLngs(pts);
    const last = pts[pts.length - 1];
    marker.setLatLng(last);
    map.panTo(last, { animate: false });
  }, [lat, lon, lat.length]);

  return <div ref={elRef} className="gps-map" />;
}
