// Leaflet map helpers for MeshCom WebDesk
// Called from Map.razor via JS interop

window.meshcomMap = (function () {
    var _map             = null;
    var _stationLayer    = null;
    var _ownLayer        = null;
    var _relayLayer      = null;
    var _coverageLayer   = null;
    var _fsplLayer       = null;
    var _lastBounds      = null;
    var _stationMarkers  = {};   // remote stations, keyed by uppercase callsign
    var _ownMarker       = null;
    var _ownKey          = null;
    var _segEntries      = [];   // drawn relay segments (for focus mode)
    var _focusCall       = null; // uppercase callsign whose links are highlighted
    var _initialFitDone  = false;
    var _readyToSave     = false;
    var _dotNet          = null;
    var STORAGE_KEY      = 'meshcom_map_view';

    // Signal thresholds; overwritten from C# (SignalHelper) via init()
    var _sig = { rssiGood: -105, rssiWeak: -115, snrGood: 0, snrWeak: -10 };

    // Popup/tooltip strings; overwritten from C# (LanguageService) via init().
    // Defaults are German so behaviour is unchanged if init() is called without i18n.
    var _i18n = {
        justNow:        'gerade',
        minAgo:          'vor {n} min',
        hAgo:            'vor {n} h',
        dAgo:            'vor {n} d',
        aiInfo:          '🤖 KI-Info',
        aiAnalyzing:     '⏳ KI analysiert…',
        upstreamHop:     'vorgelagerter Hop – Linkqualität unbekannt',
        partialPath:     'unvollst. Pfad',
        measuredRange:   '📡 Gemessene Reichweite',
        fsplRange:       'FSPL-Reichweite:',
        systemMargin:    'Systemreserve:',
        antennaHeight:   'Antennenhöhe:',
        frequency:       'Frequenz:',
        fsplNote:        'Freiraumdämpfung, ohne Geländeberücksichtigung'
    };

    function esc(s) {
        return (s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function formatAge(mins) {
        if (mins == null || mins < 0) return '';
        if (mins <  2)    return _i18n.justNow;
        if (mins <  60)   return _i18n.minAgo.replace('{n}', Math.round(mins));
        if (mins < 1440)  return _i18n.hAgo.replace('{n}', Math.round(mins / 60));
        return _i18n.dAgo.replace('{n}', Math.round(mins / 1440));
    }

    function saveView() {
        if (!_map || !_readyToSave) return;
        try {
            var c = _map.getCenter();
            localStorage.setItem(STORAGE_KEY, JSON.stringify({ lat: c.lat, lon: c.lng, zoom: _map.getZoom() }));
        } catch (e) { }
    }

    // ── Signal rating ─────────────────────────────────────────────────
    // Tier: 0 = unknown, 1 = good, 2 = fair, 3 = weak.
    // Rated by the worse of RSSI and SNR (mirrors SignalHelper.Rate in C#).
    function sigTier(rssi, snr) {
        var t = 0;
        if (rssi != null) t = Math.max(t, rssi > _sig.rssiGood ? 1 : rssi > _sig.rssiWeak ? 2 : 3);
        if (snr  != null) t = Math.max(t, snr  > _sig.snrGood  ? 1 : snr  > _sig.snrWeak  ? 2 : 3);
        return t;
    }

    function tierColor(t) {
        return t === 1 ? '#39d353' : t === 2 ? '#f0c060' : t === 3 ? '#ff6b6b' : '#b0bec5';
    }

    function tierClass(t) {
        return t === 1 ? 'sig-good' : t === 2 ? 'sig-ok' : t === 3 ? 'sig-weak' : 'sig-none';
    }

    // Lines from stations heard long ago fade out (full < 15 min → 0.3 at 24 h)
    function ageFade(mins) {
        if (mins == null) return 1;
        if (mins <= 15)   return 1;
        if (mins >= 1440) return 0.3;
        return 1 - 0.7 * (mins - 15) / (1440 - 15);
    }

    // APRS-style marker: filled circle (signal colour) + optional relay ring + callsign label
    function stationIcon(callsign, tier, hopCount, hasTelem, isGateway) {
        var sigClass   = tierClass(tier);
        var relayClass = hopCount > 1 ? ' aprs-relay-2'
                       : hopCount > 0 ? ' aprs-relay-1'
                       :                '';
        var gwClass   = isGateway ? ' aprs-gateway' : '';
        var gwIcon    = isGateway ? '<span class="aprs-gw-icon">🌐</span>' : '';
        var telemIcon = hasTelem ? '<span class="aprs-telem-icon">🌡️</span>' : '';
        var html = '<div class="aprs-wrap">'
                 + '<div class="aprs-dot ' + sigClass + relayClass + gwClass + '"></div>'
                 + '<div class="aprs-label' + (isGateway ? ' aprs-label-gateway' : '') + '">' + gwIcon + esc(callsign) + telemIcon + '</div>'
                 + '</div>';
        return L.divIcon({ className: '', html: html, iconAnchor: [6, 6] });
    }

    // Own position: gold diamond + label (+ green gateway ring when isGateway)
    function ownIcon(callsign, hasTelem, isGateway) {
        var telemIcon = hasTelem ? '<span class="aprs-telem-icon">🌡️</span>' : '';
        var gwIcon    = isGateway ? '<span class="aprs-gw-icon">🌐</span>' : '';
        var gwRing    = isGateway ? ' aprs-own-gateway' : '';
        var html = '<div class="aprs-wrap">'
                 + '<div class="aprs-dot aprs-own' + gwRing + '"></div>'
                 + '<div class="aprs-label aprs-own-label' + (isGateway ? ' aprs-label-gateway' : '') + '">' + gwIcon + esc(callsign) + telemIcon + '</div>'
                 + '</div>';
        return L.divIcon({ className: '', html: html, iconAnchor: [7, 7] });
    }

    function buildStationPopup(s) {
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
            var relayText = hops.slice(1).map(function(h) { return esc(h.trim()); }).join(' ⟶ ');
            relayLine = '<br><span style="font-size:11px;color:#8b949e">Via: ' + relayText + '</span>';
        }
        var telemLine = '';
        if (s.temp != null || s.humidity != null || s.pressure != null) {
            telemLine = '<br><span style="font-size:11px;color:#c8d8e8">';
            if (s.temp     != null) telemLine += '🌡️ ' + s.temp.toFixed(1) + '°C';
            if (s.humidity != null) telemLine += (s.temp != null ? '&nbsp;&nbsp;' : '') + '💧 ' + s.humidity.toFixed(0) + '%';
            if (s.pressure != null) telemLine += '<br>🧭 ' + s.pressure.toFixed(1) + ' hPa';
            telemLine += '</span>';
            if (s.telemMins != null) {
                telemLine += '<br><span style="font-size:10px;color:#6e7681">⏱ ' + formatAge(s.telemMins) + '</span>';
            }
        }
        var signalLine = '';
        if (s.rssi != null) {
            signalLine = '<br>RSSI: ' + s.rssi + ' dBm' + (s.snr != null ? ' / SNR ' + s.snr.toFixed(1) + ' dB' : '');
        } else if (s.snr != null) {
            signalLine = '<br>SNR: ' + s.snr.toFixed(1) + ' dB';
        }
        var aprsLink = '<br><a href="https://aprs.fi/info/a/' + encodeURIComponent(s.callsign)
            + '" target="_blank" rel="noopener" style="font-size:11px;color:#58a6ff">🔗 aprs.fi</a>';
        var aiBtn = '<br><button onclick="meshcomMap.requestAiInfo(\'' + esc(s.callsign) + '\')" '
            + 'id="ai-btn-' + esc(s.callsign.replace(/[^a-zA-Z0-9]/g,'-')) + '" '
            + 'style="margin-top:5px;font-size:11px;background:#1a3a5c;color:#79c0ff;border:1px solid #3a6a8a;border-radius:4px;padding:2px 8px;cursor:pointer">' + _i18n.aiInfo + '</button>'
            + '<div id="ai-result-' + esc(s.callsign.replace(/[^a-zA-Z0-9]/g,'-')) + '" style="font-size:11px;margin-top:4px;color:#c9d1d9;max-width:260px;white-space:pre-wrap"></div>';
        return '<b>' + esc(s.callsign) + '</b>' + (s.isGateway ? ' <span style="font-size:10px;font-weight:700;background:#0d2b1a;color:#3fb950;border-radius:3px;padding:1px 5px;margin-left:4px">GW</span>' : '') + qrzLine + badgeLine + relayLine + telemLine
            + (s.text     ? '<br><span style="font-size:12px">' + esc(s.text) + '</span>' : '')
            + signalLine
            + (s.battery  != null ? '&nbsp;🔋 ' + s.battery + '%' : '')
            + (s.alt      != null ? '<br>Alt: ' + s.alt + ' m' : '')
            + (s.locator  ? '<br><span style="font-size:11px">QTH: <code>' + esc(s.locator) + '</code></span>' : '')
            + '<br><a href="https://www.openstreetmap.org/?mlat=' + s.lat.toFixed(6) + '&mlon=' + s.lon.toFixed(6) + '&zoom=14" target="_blank" rel="noopener" style="font-size:11px;color:#58a6ff">'
            + '📍 ' + s.lat.toFixed(4) + (s.lat >= 0 ? '°N' : '°S') + ' ' + s.lon.toFixed(4) + (s.lon >= 0 ? '°E' : '°W') + '</a>'
            + aprsLink
            + aiBtn;
    }

    function buildOwnPopup(ownCallsign, info) {
        var ownPopup = '<b>' + esc(ownCallsign) + '</b>';
        if (info.posSource)
            ownPopup += '<br><span style="font-size:11px;color:#aaa">' + esc(info.isGateway && info.posSource === 'Node' ? 'GW' : info.posSource) + '</span>';
        if (info.isGateway)
            ownPopup += ' <span style="font-size:10px;font-weight:700;background:#0d2b1a;color:#3fb950;border-radius:3px;padding:1px 5px;margin-left:4px">GW</span>';
        if (info.alt      != null)
            ownPopup += '<br>Alt: ' + info.alt + ' m';
        if (info.rssi     != null) {
            ownPopup += '<br>RSSI: ' + info.rssi + ' dBm';
            if (info.snr != null) ownPopup += ' / SNR: ' + info.snr.toFixed(1) + ' dB';
        }
        if (info.temp != null || info.humidity != null || info.pressure != null) {
            ownPopup += '<br><span style="font-size:11px;color:#c8d8e8">';
            if (info.temp     != null) ownPopup += '🌡️ ' + info.temp.toFixed(1) + '°C';
            if (info.humidity != null) ownPopup += (info.temp != null ? '&nbsp;&nbsp;' : '') + '💧 ' + info.humidity.toFixed(0) + '%';
            if (info.pressure != null) ownPopup += '<br>🧭 ' + info.pressure.toFixed(1) + ' hPa';
            ownPopup += '</span>';
            if (info.telemMins != null)
                ownPopup += '<br><span style="font-size:10px;color:#6e7681">⏱ ' + formatAge(info.telemMins) + '</span>';
        }
        ownPopup += '<br>📨 RX ' + (info.rxCount || 0) + ' / TX ' + (info.txCount || 0);
        if (info.beacon) {
            ownPopup += '<br>🔵 Beacon';
            if (info.beaconNext) ownPopup += ' · ' + esc(info.beaconNext);
        }
        if (info.deviceIp)
            ownPopup += '<br><span style="font-size:10px;color:#6e7681">📡 '
                      + esc(info.deviceIp) + ':' + (info.devicePort || '') + '</span>';
        if (ownCallsign)
            ownPopup += '<br><a href="https://aprs.fi/info/a/' + encodeURIComponent(ownCallsign)
                      + '" target="_blank" rel="noopener" style="font-size:11px;color:#58a6ff">🔗 aprs.fi</a>';
        return ownPopup;
    }

    // ── Focus mode: dim all segments not involving the focused callsign ──
    function applyFocus() {
        var focus = _focusCall;
        _segEntries.forEach(function (e) {
            var dim = focus != null && e.stations.indexOf(focus) === -1;
            e.line.setStyle({ opacity: dim ? e.lineOpacity * 0.12 : e.lineOpacity });
            e.halo.setStyle({ opacity: dim ? e.haloOpacity * 0.12 : e.haloOpacity });
            if (e.arrow) {
                var el = e.arrow.getElement && e.arrow.getElement();
                if (el) el.style.opacity = dim ? 0.08 : 1;
            }
        });
    }

    function attachFocusHandlers(marker, key) {
        marker.on('popupopen',  function () { _focusCall = key; applyFocus(); });
        marker.on('popupclose', function () {
            if (_focusCall === key) { _focusCall = null; applyFocus(); }
        });
    }

    return {
        init: function (elementId, ownLat, ownLon, dotNetRef, sigThresholds, i18n) {
            if (_map) { _map.remove(); _map = null; }
            _lastBounds     = null;
            _initialFitDone = false;
            _readyToSave    = false;
            _dotNet         = dotNetRef;
            _segEntries     = [];
            _focusCall      = null;
            _ownMarker      = null;
            _ownKey         = null;
            if (sigThresholds) _sig = sigThresholds;
            if (i18n) Object.assign(_i18n, i18n);

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
            _coverageLayer = L.layerGroup();                   // not added by default
            _relayLayer    = L.layerGroup().addTo(_map);
            _stationLayer  = L.layerGroup().addTo(_map);
            _ownLayer      = L.layerGroup().addTo(_map);

            if (saved) {
                _initialFitDone = true;
                _readyToSave    = true;
            }

            _map.on('moveend zoomend', saveView);
        },

        updateMarkers: function (stations, ownCallsign, ownLat, ownLon, segments, showRelays, ownInfo) {
            if (!_map) return;
            _relayLayer.clearLayers();
            _segEntries = [];

            var bounds = [];

            // ── Aggregated relay segments ─────────────────────────────────
            if (showRelays) {
                (segments || []).forEach(function (seg) {
                    if (!seg.a || !seg.b) return;
                    var coords = [seg.a, seg.b];

                    // Weight: min 3px (new link) → max 8px (heavily used), logarithmic
                    var weight    = Math.min(3 + Math.log(Math.max(seg.count || 1, 1)) * 1.5, 8);
                    var dashArray = seg.partial ? '4, 8' : '12, 5';
                    var fade      = ageFade(seg.ageMins);

                    // Colour only the true final RF hop (quality measured at own node);
                    // upstream hops have unknown link quality → neutral blue
                    // (same blue as markers without signal data)
                    var color = seg.lastHop && (seg.rssi != null || seg.snr != null)
                              ? tierColor(sigTier(seg.rssi, seg.snr))
                              : '#58a6ff';

                    var lineOpacity = (seg.partial ? 0.50 : 0.90) * fade;
                    var haloOpacity = (seg.partial ? 0.25 : 0.55) * fade;

                    var label = '<b>' + esc(seg.from) + ' ⟶ ' + esc(seg.to) + '</b>'
                              + '<br>' + (seg.distKm != null ? seg.distKm.toFixed(1) + ' km · ' : '')
                              + (seg.count || 1) + '×'
                              + (seg.ageMins != null ? ' · ' + formatAge(seg.ageMins) : '');
                    if (seg.rssi != null || seg.snr != null) {
                        label += '<br>';
                        if (seg.rssi != null) label += 'RSSI ' + seg.rssi + ' dBm';
                        if (seg.snr  != null) label += (seg.rssi != null ? ' / ' : '') + 'SNR ' + seg.snr.toFixed(1) + ' dB';
                    }
                    if (!seg.lastHop) label += '<br><small>' + _i18n.upstreamHop + '</small>';
                    if (seg.partial)  label += '<br><i>' + _i18n.partialPath + '</i>';

                    // 1. Dark halo (drawn first = below) for contrast against map tiles
                    var halo = L.polyline(coords, {
                        color:     'rgba(0,0,0,0.75)',
                        weight:    weight + 3,
                        opacity:   haloOpacity,
                        dashArray: dashArray,
                        interactive: false
                    }).addTo(_relayLayer);

                    // 2. Coloured line on top; fresh traffic (< 2 min) animates
                    var line = L.polyline(coords, {
                        color:     color,
                        weight:    weight,
                        opacity:   lineOpacity,
                        dashArray: dashArray,
                        className: seg.ageMins != null && seg.ageMins <= 2 ? 'relay-live' : ''
                    })
                    .bindTooltip(label, { sticky: true, className: 'relay-tooltip' })
                    .addTo(_relayLayer);

                    // 3. Direction arrow at the segment midpoint (skip very short hops)
                    var arrow = null;
                    if (seg.distKm == null || seg.distKm > 0.5) {
                        var midLat = (seg.a[0] + seg.b[0]) / 2;
                        var midLon = (seg.a[1] + seg.b[1]) / 2;
                        var dLat   = seg.b[0] - seg.a[0];
                        var dLon   = (seg.b[1] - seg.a[1]) * Math.cos(midLat * Math.PI / 180);
                        var angle  = Math.atan2(-dLat, dLon) * 180 / Math.PI;
                        arrow = L.marker([midLat, midLon], {
                            icon: L.divIcon({
                                className: '',
                                html: '<div class="relay-arrow" style="transform:rotate(' + angle.toFixed(1) + 'deg);opacity:' + fade.toFixed(2) + '">➤</div>',
                                iconSize:   [16, 16],
                                iconAnchor: [8, 8]
                            }),
                            interactive: false,
                            keyboard:    false
                        }).addTo(_relayLayer);
                    }

                    _segEntries.push({
                        stations:    seg.stations || [],
                        line:        line,
                        halo:        halo,
                        arrow:       arrow,
                        lineOpacity: lineOpacity,
                        haloOpacity: haloOpacity
                    });
                });
            }

            // ── Station markers (diff update: keeps open popups alive) ────
            var seen = {};
            (stations || []).forEach(function (s) {
                if (s.lat == null || s.lon == null) return;

                var key   = s.callsign.toUpperCase();
                var icon  = stationIcon(s.callsign, sigTier(s.rssi, s.snr), s.hopCount,
                                        s.temp != null || s.humidity != null || s.pressure != null,
                                        s.isGateway);
                var popup = buildStationPopup(s);

                seen[key] = true;
                var m = _stationMarkers[key];
                if (m) {
                    m.setLatLng([s.lat, s.lon]);
                    m.setIcon(icon);
                    m.setPopupContent(popup);
                } else {
                    m = L.marker([s.lat, s.lon], { icon: icon })
                        .bindPopup(popup)
                        .addTo(_stationLayer);
                    attachFocusHandlers(m, key);
                    _stationMarkers[key] = m;
                }
                bounds.push([s.lat, s.lon]);
            });
            Object.keys(_stationMarkers).forEach(function (k) {
                if (!seen[k]) {
                    _stationLayer.removeLayer(_stationMarkers[k]);
                    delete _stationMarkers[k];
                }
            });

            // ── Own marker ────────────────────────────────────────────────
            if (ownLat != null && ownLon != null) {
                var info    = ownInfo || {};
                var oIcon   = ownIcon(ownCallsign,
                                      info.temp != null || info.humidity != null || info.pressure != null,
                                      info.isGateway);
                var oPopup  = buildOwnPopup(ownCallsign, info);
                _ownKey     = ownCallsign ? ownCallsign.toUpperCase() : null;

                if (_ownMarker) {
                    _ownMarker.setLatLng([ownLat, ownLon]);
                    _ownMarker.setIcon(oIcon);
                    _ownMarker.setPopupContent(oPopup);
                } else {
                    _ownMarker = L.marker([ownLat, ownLon], { icon: oIcon })
                        .bindPopup(oPopup)
                        .addTo(_ownLayer);
                    if (_ownKey) attachFocusHandlers(_ownMarker, _ownKey);
                }
                bounds.push([ownLat, ownLon]);
            } else if (_ownMarker) {
                _ownLayer.removeLayer(_ownMarker);
                _ownMarker = null;
                _ownKey    = null;
            }

            // Re-apply focus dimming after the relay layer was rebuilt
            if (_focusCall) applyFocus();

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

        findCallsigns: function (query) {
            if (!_map || !query) return [];
            var q = query.trim().toUpperCase();
            if (!q) return [];
            var results = [];
            Object.keys(_stationMarkers).forEach(function (key) {
                if (key.indexOf(q) !== -1) results.push(key);
            });
            if (_ownKey && _ownKey.indexOf(q) !== -1 && results.indexOf(_ownKey) === -1)
                results.push(_ownKey);
            results.sort();
            return results;
        },

        jumpToCallsign: function (callsign) {
            if (!_map || !callsign) return false;
            var key    = callsign.trim().toUpperCase();
            var marker = _stationMarkers[key] || (key === _ownKey ? _ownMarker : null);
            if (!marker) return false;
            _map.setView(marker.getLatLng(), Math.max(_map.getZoom(), 13));
            marker.openPopup();
            return true;
        },

        invalidateSize: function () { if (_map) _map.invalidateSize(); },

        // ── KI-Popup ────────────────────────────────────────────────────

        requestAiInfo: function (callsign) {
            if (!_dotNet) return;
            var safeId = callsign.replace(/[^a-zA-Z0-9]/g, '-');
            var el = document.getElementById('ai-result-' + safeId);
            var btn = document.getElementById('ai-btn-' + safeId);
            if (el)  el.innerHTML  = '<span style="color:#8b949e">' + _i18n.aiAnalyzing + '</span>';
            if (btn) btn.disabled  = true;
            _dotNet.invokeMethodAsync('OnAiPopupRequestAsync', callsign);
        },

        updatePopupAiContent: function (callsign, html) {
            var safeId = callsign.replace(/[^a-zA-Z0-9]/g, '-');
            var el  = document.getElementById('ai-result-' + safeId);
            var btn = document.getElementById('ai-btn-'    + safeId);
            if (el)  el.innerHTML  = html;
            if (btn) { btn.disabled = false; btn.textContent = _i18n.aiInfo; }
        },

        // ── Reichweiten-Wolke ─────────────────────────────────────────────
        // measuredPoints : [[lat,lon],…]  – real heard stations (blue hull)
        // ownLat/ownLon  : own position (included in measured hull)

        setCoverage: function (measuredPoints, ownLat, ownLon) {
            if (!_map) return;
            _coverageLayer.clearLayers();

            if (!measuredPoints) {
                if (_map.hasLayer(_coverageLayer)) _map.removeLayer(_coverageLayer);
                return;
            }

            // ── Measured hull (blue) ──────────────────────────────────
            var pts = (measuredPoints || []).slice();
            if (ownLat != null && ownLon != null) pts.push([ownLat, ownLon]);

            if (pts.length >= 3) {
                var hull    = convexHull(pts);
                var latlngs = hull.map(function(p) { return [p[0], p[1]]; });
                // fill
                L.polygon(latlngs, {
                    color:       '#4dabf7',
                    weight:      0,
                    fillColor:   '#4dabf7',
                    fillOpacity: 0.35,
                    interactive: false
                }).addTo(_coverageLayer);
                // border
                L.polygon(latlngs, {
                    color:       '#4dabf7',
                    weight:      3,
                    opacity:     0.95,
                    fill:        false,
                    dashArray:   '6,4',
                    interactive: false
                }).bindTooltip(_i18n.measuredRange, { sticky: true, className: 'relay-tooltip' })
                  .addTo(_coverageLayer);
            }

            if (!_map.hasLayer(_coverageLayer))
                _coverageLayer.addTo(_map);
        },

        // ── FSPL-Kreis (theoretische Freiraumreichweite) ──────────────────
        // eirpDbm      : EIRP in dBm (TX – Kabelverlust + Antennengewinn)
        // freqMhz      : Sendefrequenz in MHz
        // antennaHeightM: Antennenhöhe in m (wird für Tooltip angezeigt)
        // rxSensDbm    : Empfänger-Empfindlichkeit in dBm (typisch –120 für MeshCom)
        setFsplCircle: function (lat, lon, eirpDbm, freqMhz, antennaHeightM, rxSensDbm, systemMarginDb) {
            if (!_map) return;
            if (_fsplLayer) { _map.removeLayer(_fsplLayer); _fsplLayer = null; }
            if (lat == null || lon == null) return;

            rxSensDbm    = rxSensDbm    != null ? rxSensDbm    : -120;
            systemMarginDb = systemMarginDb != null ? systemMarginDb : 30;

            var linkBudget = eirpDbm - rxSensDbm - systemMarginDb;
            var d_km       = Math.pow(10, (linkBudget - 20 * Math.log10(freqMhz) - 32.44) / 20);
            var d_m        = d_km * 1000;

            _fsplLayer = L.layerGroup();

            var tooltipHtml =
                '📻 <b>' + _i18n.fsplRange + ' ' + (d_km >= 1 ? d_km.toFixed(1) + ' km' : Math.round(d_m) + ' m') + '</b>' +
                '<br>EIRP: ' + eirpDbm.toFixed(1) + ' dBm' +
                '<br>' + _i18n.systemMargin + ' ' + systemMarginDb + ' dB' +
                '<br>' + _i18n.antennaHeight + ' ' + antennaHeightM + ' m' +
                '<br>' + _i18n.frequency + ' ' + freqMhz + ' MHz' +
                '<br><small style="color:#8b949e">' + _i18n.fsplNote + '</small>';

            // Äußerer Kreis (max. Reichweite) – gelb, gut sichtbar
            L.circle([lat, lon], {
                radius:      d_m,
                color:       '#f0c040',
                weight:      3,
                opacity:     1.0,
                fillColor:   '#f0c040',
                fillOpacity: 0.07,
                interactive: true
            }).bindTooltip(tooltipHtml, { sticky: true, className: 'relay-tooltip' })
              .addTo(_fsplLayer);

            // Innere Ringe bei 25 / 50 / 75 %
            [0.75, 0.5, 0.25].forEach(function(frac) {
                L.circle([lat, lon], {
                    radius:      d_m * frac,
                    color:       '#f0c040',
                    weight:      1,
                    opacity:     0.5,
                    fillColor:   '#f0c040',
                    fillOpacity: 0.03,
                    interactive: false
                }).addTo(_fsplLayer);
            });

            _fsplLayer.addTo(_map);
        },

        removeFsplCircle: function () {
            if (_fsplLayer && _map) { _map.removeLayer(_fsplLayer); _fsplLayer = null; }
        },
    };

    // ── Convex Hull (Gift Wrapping) ───────────────────────────────────────
    function convexHull(points) {
        if (points.length < 3) return points;
        // Find leftmost point
        var start = 0;
        for (var i = 1; i < points.length; i++)
            if (points[i][1] < points[start][1]) start = i;

        var hull = [];
        var cur  = start;
        do {
            hull.push(points[cur]);
            var next = 0;
            for (var j = 1; j < points.length; j++) {
                if (next === cur) { next = j; continue; }
                var cross = crossProduct(points[cur], points[next], points[j]);
                if (cross < 0) next = j;
                else if (cross === 0 &&
                         dist(points[cur], points[j]) > dist(points[cur], points[next]))
                    next = j;
            }
            cur = next;
        } while (cur !== start && hull.length <= points.length);
        return hull;
    }

    function crossProduct(o, a, b) {
        return (a[0]-o[0])*(b[1]-o[1]) - (a[1]-o[1])*(b[0]-o[0]);
    }

    function dist(a, b) {
        var dx = a[0]-b[0], dy = a[1]-b[1];
        return dx*dx + dy*dy;
    }
})();
