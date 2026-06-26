// dotnet/src/TelemetryPoc.App/wwwroot/gpsmap.js
window.gpsMap = (function () {
  let map = null, line = null, marker = null;
  return {
    init: function (el) {
      map = L.map(el, { zoomControl: false }).setView([32.08, 34.78], 11);
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        { attribution: "© OpenStreetMap contributors", maxZoom: 19 }).addTo(map);
      line = L.polyline([], { color: "#e33", weight: 2 }).addTo(map);
      marker = L.circleMarker([32.08, 34.78], { radius: 5, color: "#fff", fillColor: "#e33", fillOpacity: 1 }).addTo(map);
    },
    update: function (lat, lon) {
      if (!map || lat.length === 0) return;
      const pts = lat.map((la, i) => [la, lon[i]]);
      line.setLatLngs(pts);
      const last = pts[pts.length - 1];
      marker.setLatLng(last);
      map.panTo(last, { animate: false });
    }
  };
})();
