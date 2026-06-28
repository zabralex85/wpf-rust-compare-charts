export interface LineStringFeature {
  type: "Feature";
  properties: Record<string, never>;
  geometry: { type: "LineString"; coordinates: [number, number][] };
}

export function trackToGeoJSON(lat: number[], lon: number[]): LineStringFeature {
  const n = Math.min(lat.length, lon.length);
  const coordinates: [number, number][] = [];
  for (let i = 0; i < n; i++) coordinates.push([lon[i], lat[i]]);
  return { type: "Feature", properties: {}, geometry: { type: "LineString", coordinates } };
}
