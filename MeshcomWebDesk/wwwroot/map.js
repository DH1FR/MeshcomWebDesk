// Leaflet map helpers for MeshCom WebDesk
// Called from Map.razor via JS interop

window.meshcomMap = (function () {
    var _map             = null;
    var _stationLayer    = null;
    var _ownLayer        = null;
    var _relayLayer      = null;
    var _lastBounds      = null;
    var _stationMarkers  = {};
    var _initialFitDone  = false;
    var _readyToSave     = false;
    var STORAGE_KEY      = 'meshcom_map_view';

    function esc(s) {
        return (s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function formatTelemAge(mins) {
        if (mins == null || mins < 0) return '';
        if (mins <  2)    return 'gerade';
        if (mins <  60)   return 'vor ' + Math.round(mins)      + '\u202Fmin';
        if (mins < 1440)  return 'vor ' + Math.round(mins / 60) + '\u202Fh';
        return 'vor ' + Math.round(mins / 1440) + '\u202Fd';
    }

    function saveView() {
        if (!_map || !_readyToSave) return;
        try {
            var c = _map.getCenter();
            localStorage.setItem(STORAGE_KEY, JSON.stringify({ lat: c.lat, lon: c.lng, zoom: _map.getZoom() }));
        } catch (e) { }
    }

    // APRS-style marker: filled circle (signal colour) + optional relay ring + callsign label
    function stationIcon(callsign, rssi, hopCount, hasTelem) {
        var sigClass = rssi == null ? 'sig-none'
                     : rssi > -90  ? 'sig-good'
                     : rssi > -105 ? 'sig-ok'
                     :               'sig-weak';
        var relayClass = hopCount > 1 ? ' aprs-relay-2'
                       : hopCount > 0 ? ' aprs-relay-1'
                       :                '';
        var telemIcon = hasTelem ? '<span class="aprs-telem-icon">\uD83C\uDF21\uFE0F</span>' : '';
        var html = '<div class="aprs-wrap">'
                 + '<div class="aprs-dot ' + sigClass + relayClass + '"></div>'
                 + '<div class="aprs-label">' + esc(callsign) + telemIcon + '</div>'
                 + '</div>';
        return L.divIcon({ className: '', html: html, iconAnchor: [6, 6] });
    }

    // Own position: gold diamond + label
    function ownIcon(callsign, hasTelem) {
        var telemIcon = hasTelem ? '<span class="aprs-telem-icon">\uD83C\uDF21\uFE0F</span>' : '';
        var html = '<div class="aprs-wrap">'
                 + '<div class="aprs-dot aprs-own"></div>'
                 + '<div class="aprs-label aprs-own-label">' + esc(callsign) + telemIcon + '</div>'
                 + '</div>';
        return L.divIcon({ className: '', html: html, iconAnchor: [7, 7] });
    }

    // RSSI → line colour (vivid versions for better map contrast)
    function rssiColor(rssi) {
        if (rssi == null)    return '#b0bec5';
        if (rssi > -90)      return '#39d353';
        if (rssi > -105)     return '#f0c060';
        return '#ff6b6b';
    }

    return {
        init: function (elementId, ownLat, ownLon) {
            if (_map) { _map.remove(); _map = null; }
            _lastBounds     = null;
            _initialFitDone = false;
            _readyToSave    = false;

            var saved = null;
            try { saved = JSON.parse(localStorage.getItem(STORAGE_KEY)); } catch (e) { }

            var startLat  = (saved && saved.lat  != null) ? saved.lat  : (ownLat  != null ? ownLat  : 47.5);
            var startLon  = (saved && saved.lon  != null) ? saved.lon  : (ownLon  != null ? ownLon  : 14.0);
            var startZoom = (saved && saved.zoom != null) ? saved.zoom : 6;

            _stationMarkers = {};
            _map = L.map(elementId).setView([startLat, startLon], startZoom);
            L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>',
                maxZoom: 19
            }).addTo(_map);
            _relayLayer   = L.layerGroup().addTo(_map);   // drawn first (below stations)
            _stationLayer = L.layerGroup().addTo(_map);
            _ownLayer     = L.layerGroup().addTo(_map);

            if (saved) {
                _initialFitDone = true;
                _readyToSave    = true;
            }

            _map.on('moveend zoomend', saveView);
        },

        updateMarkers: function (stations, ownCallsign, ownLat, ownLon, relayLines, showRelays, ownInfo) {
            if (!_map) return;
            _stationLayer.clearLayers();
            _ownLayer.clearLayers();
            _relayLayer.clearLayers();
            _stationMarkers = {};

            var bounds = [];

            // ── Relay polylines ───────────────────────────────────────────
            if (showRelays) {
                (relayLines || []).forEach(function (line) {
                    if (!line.coords || line.coords.length < 2) return;

                    // Weight: min 3px (new path) → max 8px (heavily used), logarithmic
                    var weight    = Math.min(3 + Math.log(Math.max(line.count || 1, 1)) * 1.5, 8);
                    var dashArray = line.partial ? '4, 8' : '12, 5';
                    var opacity   = line.partial ? 0.50  : 0.90;
                    var color     = rssiColor(line.rssi);

                    var label = esc(line.label);
                    if (line.partial) label += ' <i>(unvollst. Pfad)</i>';
                    if (line.count > 1) label += ' (' + line.count + '\xd7)';

                    // 1. Dark halo (drawn first = below) for contrast against map tiles
                    L.polyline(line.coords, {
                        color:     'rgba(0,0,0,0.75)',
                        weight:    weight + 3,
                        opacity:   line.partial ? 0.25 : 0.55,
                        dashArray: dashArray,
                        interactive: false
                    }).addTo(_relayLayer);

                    // 2. Coloured line on top
                    L.polyline(line.coords, {
                        color:     color,
                        weight:    weight,
                        opacity:   opacity,
                        dashArray: dashArray
                    })
                    .bindTooltip(label, { sticky: true, className: 'relay-tooltip' })
                    .addTo(_relayLayer);
                });
            }

            // ── Station markers ───────────────────────────────────────────            }

            // ── Station markers ───────────────────────────────────────────
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
                var badgeLine = '';
                if (s.hwName || s.firmware) {
                    badgeLine = '<br>';
                    if (s.hwName)   badgeLine += '<span style="display:inline-block;font-size:10px;font-weight:600;background:#0f3460;color:#79c0ff;border-radius:3px;padding:1px 4px;margin-right:3px">' + esc(s.hwName) + '</span>';
                    if (s.firmware) badgeLine += '<span style="display:inline-block;font-size:10px;font-weight:600;background:#1a2d1a;color:#7ee787;border-radius:3px;padding:1px 4px">' + esc(s.firmware) + '</span>';
                }
                var relayLine = '';
                if (s.hopCount > 0 && s.relayPath) {
                    var hops = s.relayPath.split(',');
                    var relayText = hops.slice(1).map(function(h) { return esc(h.trim()); }).join(' \u27f6 ');
                    relayLine = '<br><span style="font-size:11px;color:#8b949e">Via: ' + relayText + '</span>';
                }
                var telemLine = '';
                if (s.temp != null || s.humidity != null || s.pressure != null) {
                    telemLine = '<br><span style="font-size:11px;color:#c8d8e8">';
                    if (s.temp     != null) telemLine += '\uD83C\uDF21\uFE0F\u202F' + s.temp.toFixed(1) + '\u00b0C';
                    if (s.humidity != null) telemLine += (s.temp != null ? '&nbsp;&nbsp;' : '') + '\uD83D\uDCA7\u202F' + s.humidity.toFixed(0) + '%';
                    if (s.pressure != null) telemLine += '<br>\uD83E\uDDED\u202F' + s.pressure.toFixed(1) + '\u202FhPa';
                    telemLine += '</span>';
                    if (s.telemMins != null) {
                        telemLine += '<br><span style="font-size:10px;color:#6e7681">\u23f1\u202F' + formatTelemAge(s.telemMins) + '</span>';
                    }
                }
                var aprsLink = '<br><a href="https://aprs.fi/info/a/' + encodeURIComponent(s.callsign)
                    + '" target="_blank" rel="noopener" style="font-size:11px;color:#58a6ff">🔗 aprs.fi</a>';
                var popup = '<b>' + esc(s.callsign) + '</b>' + qrzLine + badgeLine + relayLine + telemLine
                    + (s.text     ? '<br><span style="font-size:12px">' + esc(s.text) + '</span>' : '')
                    + (s.rssi     != null ? '<br>RSSI: ' + s.rssi + ' dBm' : '')
                    + (s.battery  != null ? '&nbsp;\ud83d\udd0b ' + s.battery + '%' : '')
                    + (s.alt      != null ? '<br>Alt: ' + s.alt + ' m' : '')
                    + aprsLink;

                var _m = L.marker([s.lat, s.lon], { icon: stationIcon(s.callsign, s.rssi, s.hopCount, s.temp != null || s.humidity != null || s.pressure != null) })
                    .bindPopup(popup)
                    .addTo(_stationLayer);
                _stationMarkers[s.callsign.toUpperCase()] = _m;
                bounds.push([s.lat, s.lon]);
            });

            if (ownLat != null && ownLon != null) {
                var info = ownInfo || {};
                var ownPopup = '<b>' + esc(ownCallsign) + '</b>';
                if (info.posSource)
                    ownPopup += '<br><span style="font-size:11px;color:#aaa">' + esc(info.posSource) + '</span>';
                if (info.alt      != null)
                    ownPopup += '<br>Alt: ' + info.alt + ' m';
                if (info.rssi     != null) {
                    ownPopup += '<br>RSSI: ' + info.rssi + ' dBm';
                    if (info.snr != null) ownPopup += ' / SNR: ' + info.snr.toFixed(1) + ' dB';
                }
                if (info.temp != null || info.humidity != null || info.pressure != null) {
                    ownPopup += '<br><span style="font-size:11px;color:#c8d8e8">';
                    if (info.temp     != null) ownPopup += '\uD83C\uDF21\uFE0F\u202F' + info.temp.toFixed(1) + '\u00b0C';
                    if (info.humidity != null) ownPopup += (info.temp != null ? '&nbsp;&nbsp;' : '') + '\uD83D\uDCA7\u202F' + info.humidity.toFixed(0) + '%';
                    if (info.pressure != null) ownPopup += '<br>\uD83E\uDDED\u202F' + info.pressure.toFixed(1) + '\u202FhPa';
                    ownPopup += '</span>';
                    if (info.telemMins != null)
                        ownPopup += '<br><span style="font-size:10px;color:#6e7681">\u23f1\u202F' + formatTelemAge(info.telemMins) + '</span>';
                }
                ownPopup += '<br>\uD83D\uDCE8 RX ' + (info.rxCount || 0) + ' / TX ' + (info.txCount || 0);
                if (info.beacon) {
                    ownPopup += '<br>\uD83D\uDD35 Beacon';
                    if (info.beaconNext) ownPopup += ' \u00b7 ' + esc(info.beaconNext);
                }
                if (info.deviceIp)
                    ownPopup += '<br><span style="font-size:10px;color:#6e7681">\uD83D\uDCE1 '
                              + esc(info.deviceIp) + ':' + (info.devicePort || '') + '</span>';
                if (ownCallsign)
                    ownPopup += '<br><a href="https://aprs.fi/info/a/' + encodeURIComponent(ownCallsign)
                              + '" target="_blank" rel="noopener" style="font-size:11px;color:#58a6ff">🔗 aprs.fi</a>';

                var _ownM = L.marker([ownLat, ownLon], { icon: ownIcon(ownCallsign, info.temp != null || info.humidity != null || info.pressure != null) })
                    .bindPopup(ownPopup)
                    .addTo(_ownLayer);
                if (ownCallsign)
                    _stationMarkers[ownCallsign.toUpperCase()] = _ownM;
                bounds.push([ownLat, ownLon]);
            }

            if (bounds.length > 0) _lastBounds = bounds.slice();

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
                _readyToSave = true;
            }
        },

        fitAll: function () {
            if (!_map || !_lastBounds || _lastBounds.length === 0) return;
            if (_lastBounds.length === 1) {
                _map.setView(_lastBounds[0], 11);
            } else {
                _map.fitBounds(_lastBounds, { padding: [40, 40], maxZoom: 12 });
            }
        },

        fitEurope: function () {
            if (!_map) return;
            _map.fitBounds([[34, -12], [72, 45]]);
        },

        fitOwn: function (lat, lon, km) {
            if (!_map) return;
            var r    = km || 50;
            var dLat = r / 111.0;
            var dLon = r / (111.0 * Math.cos(lat * Math.PI / 180));
            _map.fitBounds([
                [lat - dLat, lon - dLon],
                [lat + dLat, lon + dLon]
            ]);
        },

        searchCallsign: function (callsign) {
            if (!_map || !callsign) return false;
            var key    = callsign.trim().toUpperCase();
            var marker = _stationMarkers[key];
            if (!marker) return false;
            _map.setView(marker.getLatLng(), Math.max(_map.getZoom(), 13));
            marker.openPopup();
            return true;
        },

        invalidateSize: function () { if (_map) _map.invalidateSize(); }
    };
})();
