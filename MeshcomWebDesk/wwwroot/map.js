// Leaflet map helpers for MeshCom WebDesk
// Called from Map.razor via JS interop

window.meshcomMap = (function () {
    var _map          = null;
    var _stationLayer = null;
    var _ownLayer     = null;
    var _lastBounds   = null;   // saved after every updateMarkers for fitAll()
    var _initialFitDone = false; // fit once on first load, then user controls zoom

    function esc(s) {
        return (s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    // APRS-style: filled circle (signal-colour) + callsign label below
    function stationIcon(callsign, rssi) {
        var sigClass = rssi == null ? 'sig-none'
                     : rssi > -90  ? 'sig-good'
                     : rssi > -105 ? 'sig-ok'
                     :               'sig-weak';
        var html = '<div class="aprs-wrap">'
                 + '<div class="aprs-dot ' + sigClass + '"></div>'
                 + '<div class="aprs-label">' + esc(callsign) + '</div>'
                 + '</div>';
        return L.divIcon({ className: '', html: html, iconAnchor: [6, 6] });
    }

    // Own position: gold diamond + label
    function ownIcon(callsign) {
        var html = '<div class="aprs-wrap">'
                 + '<div class="aprs-dot aprs-own"></div>'
                 + '<div class="aprs-label aprs-own-label">' + esc(callsign) + '</div>'
                 + '</div>';
        return L.divIcon({ className: '', html: html, iconAnchor: [7, 7] });
    }

    return {
        init: function (elementId, centerLat, centerLon, zoom) {
            if (_map) { _map.remove(); _map = null; }
            _lastBounds     = null;
            _initialFitDone = false;
            _map = L.map(elementId).setView([centerLat, centerLon], zoom);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>',
                maxZoom: 19
            }).addTo(_map);
            _stationLayer = L.layerGroup().addTo(_map);
            _ownLayer     = L.layerGroup().addTo(_map);
        },

        updateMarkers: function (stations, ownCallsign, ownLat, ownLon) {
            if (!_map) return;
            _stationLayer.clearLayers();
            _ownLayer.clearLayers();

            var bounds = [];

            (stations || []).forEach(function (s) {
                if (s.lat == null || s.lon == null) return;
                var popup = '<b>' + esc(s.callsign) + '</b>'
                    + (s.text     ? '<br><span style="font-size:12px">' + esc(s.text) + '</span>' : '')
                    + (s.rssi     != null ? '<br>RSSI: ' + s.rssi + ' dBm' : '')
                    + (s.battery  != null ? '&nbsp;🔋 ' + s.battery + '%' : '')
                    + (s.alt      != null ? '<br>Alt: ' + s.alt + ' m' : '');
                L.marker([s.lat, s.lon], { icon: stationIcon(s.callsign, s.rssi) })
                    .bindPopup(popup)
                    .addTo(_stationLayer);
                bounds.push([s.lat, s.lon]);
            });

            if (ownLat != null && ownLon != null) {
                L.marker([ownLat, ownLon], { icon: ownIcon(ownCallsign) })
                    .bindPopup('<b>' + esc(ownCallsign) + '</b><br>(eigene Position)')
                    .addTo(_ownLayer);
                bounds.push([ownLat, ownLon]);
            }

            // Save bounds for manual fitAll button
            if (bounds.length > 0) _lastBounds = bounds.slice();

            // Auto-fit only once on the very first load with data
            if (!_initialFitDone && bounds.length > 0) {
                _initialFitDone = true;
                if (bounds.length === 1) {
                    _map.setView(bounds[0], 11);
                } else {
                    _map.fitBounds(bounds, { padding: [40, 40], maxZoom: 12 });
                }
            }
        },

        // Zoom to all visible stations + own position
        fitAll: function () {
            if (!_map || !_lastBounds || _lastBounds.length === 0) return;
            if (_lastBounds.length === 1) {
                _map.setView(_lastBounds[0], 11);
            } else {
                _map.fitBounds(_lastBounds, { padding: [40, 40], maxZoom: 12 });
            }
        },

        // Zoom to Europe overview
        fitEurope: function () {
            if (!_map) return;
            _map.fitBounds([[34, -12], [72, 45]]);
        },

        // Zoom to a circle of `km` radius around own position
        fitOwn: function (lat, lon, km) {
            if (!_map) return;
            var r       = km || 50;
            // 1 degree latitude ≈ 111 km; longitude correction via cos(lat)
            var dLat    = r / 111.0;
            var dLon    = r / (111.0 * Math.cos(lat * Math.PI / 180));
            _map.fitBounds([
                [lat - dLat, lon - dLon],
                [lat + dLat, lon + dLon]
            ]);
        },

        invalidateSize: function () { if (_map) _map.invalidateSize(); }
    };
})();

