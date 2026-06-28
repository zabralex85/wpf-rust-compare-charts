import type { StyleSpecification } from "maplibre-gl";

/**
 * Returns a dark MapLibre vector style wired to local tile/glyph endpoints.
 *
 * Palette follows the INU dark theme:
 *   background #0a0e14  water #0d1a24  land dark  roads muted grey
 * Source schema: OpenMapTiles (source-layers: water, landcover, landuse,
 *   transportation, building, place).
 *
 * Hard-coded hex colours are intentional — style JSON is data, not component
 * CSS; CSS variables cannot be used inside a StyleSpecification object.
 */
export function mapStyle(tilesBase: string): StyleSpecification {
  return {
    version: 8,
    sources: {
      basemap: {
        type: "vector",
        url: `${tilesBase}/tiles.json`,
      },
    },
    glyphs: `${tilesBase}/glyphs/{fontstack}/{range}.pbf`,
    layers: [
      // ── Background ─────────────────────────────────────────────────────────
      {
        id: "background",
        type: "background",
        paint: {
          "background-color": "#0a0e14",
        },
      },
      // ── Water ──────────────────────────────────────────────────────────────
      {
        id: "water",
        type: "fill",
        source: "basemap",
        "source-layer": "water",
        paint: {
          "fill-color": "#0d1a24",
        },
      },
      // ── Landcover (parks, vegetation) ──────────────────────────────────────
      {
        id: "landcover",
        type: "fill",
        source: "basemap",
        "source-layer": "landcover",
        paint: {
          "fill-color": "#0c1118",
          "fill-opacity": 0.8,
        },
      },
      // ── Landuse (residential, industrial) ──────────────────────────────────
      {
        id: "landuse",
        type: "fill",
        source: "basemap",
        "source-layer": "landuse",
        paint: {
          "fill-color": "#111820",
          "fill-opacity": 0.6,
        },
      },
      // ── Transportation casing (halo beneath road line) ─────────────────────
      {
        id: "transportation-casing",
        type: "line",
        source: "basemap",
        "source-layer": "transportation",
        paint: {
          "line-color": "#0a0e14",
          "line-width": 3,
          "line-gap-width": 0,
        },
      },
      // ── Transportation line (road surface) ─────────────────────────────────
      {
        id: "transportation",
        type: "line",
        source: "basemap",
        "source-layer": "transportation",
        paint: {
          "line-color": "#2a2f3a",
          "line-width": 1.5,
        },
      },
      // ── Buildings ──────────────────────────────────────────────────────────
      {
        id: "building",
        type: "fill",
        source: "basemap",
        "source-layer": "building",
        paint: {
          "fill-color": "#141c24",
          "fill-opacity": 0.7,
        },
      },
      // ── Place labels ───────────────────────────────────────────────────────
      {
        id: "place-label",
        type: "symbol",
        source: "basemap",
        "source-layer": "place",
        layout: {
          // cast: ["get", string] is assignable to ExpressionSpecification
          // (the ['get', string | ExpressionSpecification, ExpressionSpecification?] member)
          "text-field": ["get", "name"] as ["get", string],
          "text-font": ["Noto Sans Regular"],
          "text-size": 12,
        },
        paint: {
          "text-color": "#b0b8c4",
          "text-halo-color": "#0a0e14",
          "text-halo-width": 1,
        },
      },
    ],
  };
}
