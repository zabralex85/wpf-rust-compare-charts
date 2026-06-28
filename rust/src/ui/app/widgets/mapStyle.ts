import type { ExpressionSpecification, StyleSpecification } from "maplibre-gl";

// Prefer the romanized name (Noto Sans Regular has no Hebrew glyphs) → fall back
// to the native `name`. tilemaker's OpenMapTiles output provides `name:latin`.
const NAME_LATIN: ExpressionSpecification = ["coalesce", ["get", "name:latin"], ["get", "name"]];

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
    glyphs: `${tilesBase}/glyphs/{fontstack}/{range}.pbf`,
    sources: {
      basemap: {
        type: "vector",
        url: `${tilesBase}/tiles.json`,
      },
    },
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
          "fill-color": "#16384f",
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
          "line-color": "#5b6470",
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
          "fill-color": "#232d38",
          "fill-opacity": 0.8,
        },
      },
      // ── Road name labels (follow the street, visible at street zoom) ───────
      {
        id: "road-label",
        type: "symbol",
        source: "basemap",
        "source-layer": "transportation_name",
        layout: {
          "symbol-placement": "line",
          "text-field": NAME_LATIN,
          "text-font": ["Noto Sans Regular"],
          "text-size": 11,
        },
        paint: {
          "text-color": "#8a99ad",
          "text-halo-color": "#0a0e14",
          "text-halo-width": 1.2,
        },
      },
      // ── Place labels (towns/neighbourhoods, shown when zoomed out) ──────────
      {
        id: "place-label",
        type: "symbol",
        source: "basemap",
        "source-layer": "place",
        layout: {
          "text-field": NAME_LATIN,
          "text-font": ["Noto Sans Regular"],
          "text-size": 13,
        },
        paint: {
          "text-color": "#aebccd",
          "text-halo-color": "#0a0e14",
          "text-halo-width": 1.4,
        },
      },
    ],
  };
}
