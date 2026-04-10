// Leaflet map helpers for MeshCom WebDesk
// Called from Map.razor via JS interop

window.meshcomMap = (function () {
    var _map          = null;
    var _stationLayer = null;
    var _ownLayer     = null;
    var _lastBounds   = null;   // saved after every updateMarkers for fitAll()
    var _initialFitDone = false; // fit once on first load, then user controls zoom
    var _readyToSave    = false; // only persist view after initial fit is complete
    var STORAGE_KEY     = 'meshcom_map_view';

    function esc(s) {
        return (s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function saveView() {
        if (!_map || !_readyToSave) return;
        try {
            var c = _map.getCenter();
            localStorage.setItem(STORAGE_KEY, JSON.stringify({ lat: c.lat, lon: c.lng, zoom: _map.getZoom() }));
        } catch (e) { }
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
        init: function (elementId, ownLat, ownLon) {
            if (_map) { _map.remove(); _map = null; }
            _lastBounds     = null;
            _initialFitDone = false;
            _readyToSave    = false;

            // Restore last saved view (persists across navigation / page reloads)
            var saved = null;
            try { saved = JSON.parse(localStorage.getItem(STORAGE_KEY)); } catch (e) { }

            var startLat  = (saved && saved.lat  != null) ? saved.lat  : (ownLat  != null ? ownLat  : 47.5);
            var startLon  = (saved && saved.lon  != null) ? saved.lon  : (ownLon  != null ? ownLon  : 14.0);
            var startZoom = (saved && saved.zoom != null) ? saved.zoom : 6;

            _map = L.map(elementId).setView([startLat, startLon], startZoom);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>',
                maxZoom: 19
            }).addTo(_map);
            _stationLayer = L.layerGroup().addTo(_map);
            _ownLayer     = L.layerGroup().addTo(_map);

            if (saved) {
                // Saved view restored – skip first-run auto-fit, start saving immediately
                _initialFitDone = true;
                _readyToSave    = true;
            }

            _map.on('moveend zoomend', saveView);
        },

        updateMarkers: function (stations, ownCallsign, ownLat, ownLon) {
            if (!_map) return;
            _stationLayer.clearLayers();
            _ownLayer.clearLayers();

            var bounds = [];

            (stations || []).forEach(function (s) {
                if (s.lat == null || s.lon == null) return;
                var qrzLine = '';
                if (s.qrzName || s.qrzLoc) {
                    qrzLine = '<br><span style="font-size:11px;color:#aaa">';
                    if (s.qrzName) qrzLine += esc(s.qrzName);
                    if (s.qrzName && s.qrzLoc) qrzLine += ', ';
                    if (s.qrzLoc)  qrzLine += esc(s.qrzLoc);
                    qrzLine += '</span>';
                }
                var popup = '<b>' + esc(s.callsign) + '</b>' + qrzLine
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

            // First open (no saved state): zoom to 50 km around own position,
            // or fall back to fitting all visible stations.
            if (!_initialFitDone && bounds.length > 0) {
                _initialFitDone = true;
                if (ownLat != null && ownLon != null) {
                    var r    = 50;
                    var dLat = r / 111.0;
                    var dLon = r / (111.0 * Math.cos(ownLat * Math.PI / 180));
                    _map.fitBounds([
                        [ownLat - dLat, ownLon - dLon],
                        [ownLat + dLat, ownLon + dLon]
                    ]);
                } else if (bounds.length === 1) {
                    _map.setView(bounds[0], 11);
                } else {
                    _map.fitBounds(bounds, { padding: [40, 40], maxZoom: 12 });
                }
                _readyToSave = true; // begin persisting view after initial fit
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

