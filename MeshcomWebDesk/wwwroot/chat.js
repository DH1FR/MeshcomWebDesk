'use strict';

window.meshcomChat = (function () {
    const STORAGE_KEY = 'meshcom-monitor-height';

    // Berechnet die JSON-kodierten Bytes des msg-Werts, so wie C# JsonSerializer sie sendet.
    // ASCII-Zeichen: 1 Byte. BMP-Non-ASCII (z.B. °, ü): \uXXXX = 6 Bytes.
    // Emojis (surrogate pairs): \uXXXX\uXXXX = 12 Bytes.
    function csharpJsonMsgBytes(text) {
        var bytes = 0;
        for (var i = 0; i < text.length; i++) {
            var c = text.charCodeAt(i);
            if (c < 0x80) {
                bytes += 1;
            } else if (c >= 0xD800 && c <= 0xDBFF) {
                bytes += 12; // surrogate pair (Emoji): \uXXXX\uXXXX
                i++;          // low surrogate überspringen
            } else {
                bytes += 6;  // BMP-Non-ASCII: \uXXXX
            }
        }
        return bytes;
    }

    function updateSendCounter(counter, text) {
        var chars     = text.length;
        var jsonBytes = csharpJsonMsgBytes(text);
        var tooLarge  = chars > 149 || jsonBytes > 200;
        counter.textContent = chars + '/149';
        counter.title       = jsonBytes + 'B / 200B JSON';
        counter.className   = 'char-counter' +
            (chars >= 145 || jsonBytes > 200 ? ' char-danger' :
             chars >= 130 || jsonBytes >= 180 ? ' char-warn' : '');

        // Senden-Button sperren wenn Paket zu groß – aber Cooldown-Sperre nicht überschreiben
        var bar = counter.closest('.send-bar');
        var btn = bar && bar.querySelector('.btn-send');
        if (btn && !btn.classList.contains('btn-send-cooldown')) {
            btn.disabled = tooLarge;
        }
    }

    function initResizer() {
        const divider   = document.getElementById('pane-divider');
        const lowerPane = document.getElementById('lower-pane');
        if (!divider || !lowerPane) return;

        // Guard: only bind once per DOM element lifetime
        if (divider.dataset.resizerInit) return;
        divider.dataset.resizerInit = '1';

        // Restore previously saved height
        const saved = parseInt(localStorage.getItem(STORAGE_KEY) || '0', 10);
        if (saved > 0) {
            lowerPane.style.height = saved + 'px';
            lowerPane.style.flex   = 'none';
        }

        let startY = 0, startH = 0;

        function onMove(e) {
            if (lowerPane.classList.contains('lower-pane-collapsed')) return;
            const y    = e.touches ? e.touches[0].clientY : e.clientY;
            const contH = lowerPane.parentElement.getBoundingClientRect().height;
            const newH  = Math.max(40, Math.min(contH - 80, startH + (startY - y)));
            lowerPane.style.height = newH + 'px';
            lowerPane.style.flex   = 'none';
            if (e.cancelable) e.preventDefault();
        }

        function onUp() {
            localStorage.setItem(STORAGE_KEY,
                Math.round(lowerPane.getBoundingClientRect().height));
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup',   onUp);
            document.removeEventListener('touchmove', onMove);
            document.removeEventListener('touchend',  onUp);
        }

        function onDown(e) {
            if (lowerPane.classList.contains('lower-pane-collapsed')) return;
            startY = e.touches ? e.touches[0].clientY : e.clientY;
            startH = lowerPane.getBoundingClientRect().height;
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup',   onUp);
            document.addEventListener('touchmove', onMove, { passive: false });
            document.addEventListener('touchend',  onUp);
            e.preventDefault();
        }

        divider.addEventListener('mousedown',  onDown);
        divider.addEventListener('touchstart', onDown, { passive: false });
    }

    function initTabDrag(dotNetRef) {
        var tabBar = document.querySelector('.tab-bar');
        if (!tabBar || tabBar.dataset.dragInit) return;
        tabBar.dataset.dragInit = '1';

        var dragKey  = null;
        var hoverBtn = null;

        tabBar.addEventListener('dragstart', function (e) {
            var btn = e.target.closest('[data-tab-key]');
            if (!btn) return;
            dragKey = btn.dataset.tabKey;
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', dragKey);
            btn.classList.add('tab-dragging');
        });

        tabBar.addEventListener('dragover', function (e) {
            var btn = e.target.closest('[data-tab-key]');
            if (!btn || !dragKey) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            if (btn !== hoverBtn) {
                if (hoverBtn) hoverBtn.classList.remove('tab-drag-over');
                hoverBtn = btn.dataset.tabKey !== dragKey ? btn : null;
                if (hoverBtn) hoverBtn.classList.add('tab-drag-over');
            }
        });

        tabBar.addEventListener('dragleave', function (e) {
            var btn = e.target.closest('[data-tab-key]');
            if (btn && btn === hoverBtn) {
                btn.classList.remove('tab-drag-over');
                hoverBtn = null;
            }
        });

        tabBar.addEventListener('drop', function (e) {
            e.preventDefault();
            var btn = e.target.closest('[data-tab-key]');
            if (hoverBtn) { hoverBtn.classList.remove('tab-drag-over'); hoverBtn = null; }
            tabBar.querySelectorAll('[data-tab-key].tab-dragging').forEach(function (b) {
                b.classList.remove('tab-dragging');
            });
            if (!btn || !dragKey || btn.dataset.tabKey === dragKey) { dragKey = null; return; }
            var toKey   = btn.dataset.tabKey;
            var fromKey = dragKey;
            dragKey = null;
            dotNetRef.invokeMethodAsync('MoveTab', fromKey, toKey);
        });

        tabBar.addEventListener('dragend', function () {
            if (hoverBtn) { hoverBtn.classList.remove('tab-drag-over'); hoverBtn = null; }
            tabBar.querySelectorAll('[data-tab-key].tab-dragging').forEach(function (b) {
                b.classList.remove('tab-dragging');
            });
            dragKey = null;
        });
    }

    function initQuickTextDrag(dotNetRef) {
        var flyout = document.getElementById('qt-flyout');
        if (!flyout) return;

        // Re-init every time flyout opens (DOM is recreated by Blazor)
        var dragIdx  = -1;
        var hoverBtn = null;

        flyout.addEventListener('dragstart', function (e) {
            var btn = e.target.closest('[data-qt-idx]');
            if (!btn) return;
            dragIdx = parseInt(btn.dataset.qtIdx, 10);
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', '' + dragIdx);
            btn.classList.add('qt-dragging');
        });

        flyout.addEventListener('dragover', function (e) {
            var btn = e.target.closest('[data-qt-idx]');
            if (!btn || dragIdx < 0) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            if (btn !== hoverBtn) {
                if (hoverBtn) hoverBtn.classList.remove('qt-drag-over');
                var idx = parseInt(btn.dataset.qtIdx, 10);
                hoverBtn = idx !== dragIdx ? btn : null;
                if (hoverBtn) hoverBtn.classList.add('qt-drag-over');
            }
        });

        flyout.addEventListener('dragleave', function (e) {
            var btn = e.target.closest('[data-qt-idx]');
            if (btn && btn === hoverBtn) {
                btn.classList.remove('qt-drag-over');
                hoverBtn = null;
            }
        });

        flyout.addEventListener('drop', function (e) {
            e.preventDefault();
            var btn = e.target.closest('[data-qt-idx]');
            if (hoverBtn) { hoverBtn.classList.remove('qt-drag-over'); hoverBtn = null; }
            flyout.querySelectorAll('[data-qt-idx].qt-dragging').forEach(function (b) {
                b.classList.remove('qt-dragging');
            });
            if (!btn || dragIdx < 0) { dragIdx = -1; return; }
            var toIdx   = parseInt(btn.dataset.qtIdx, 10);
            var fromIdx = dragIdx;
            dragIdx = -1;
            if (fromIdx !== toIdx) {
                dotNetRef.invokeMethodAsync('MoveQuickText', fromIdx, toIdx);
            }
        });

        flyout.addEventListener('dragend', function () {
            if (hoverBtn) { hoverBtn.classList.remove('qt-drag-over'); hoverBtn = null; }
            flyout.querySelectorAll('[data-qt-idx].qt-dragging').forEach(function (b) {
                b.classList.remove('qt-dragging');
            });
            dragIdx = -1;
        });
    }

    return {
        initResizer,
        initTabDrag,
        initQuickTextDrag,
        getActiveTab:      () => localStorage.getItem('meshcom-active-tab') || '',
        setActiveTab:      (key) => {
            if (key) localStorage.setItem('meshcom-active-tab', key);
            else     localStorage.removeItem('meshcom-active-tab');
        },
        getMonitorVisible: () => {
            var v = localStorage.getItem('meshcom-monitor-visible');
            return v === null ? null : v === '1';
        },
        setMonitorVisible: (visible) =>
            localStorage.setItem('meshcom-monitor-visible', visible ? '1' : '0'),
        getVoiceEnabled: () => {
            var v = localStorage.getItem('meshcom-voice-enabled');
            return v === null ? null : v === '1';
        },
        setVoiceEnabled: (enabled) =>
            localStorage.setItem('meshcom-voice-enabled', enabled ? '1' : '0'),
        getTabOrder: () => {
            var v = localStorage.getItem('meshcom-tab-order');
            try { return v ? JSON.parse(v) : []; } catch (e) { return []; }
        },
        setTabOrder: (keys) =>
            localStorage.setItem('meshcom-tab-order', JSON.stringify(keys || [])),
        getSettingsSections: () => localStorage.getItem('meshcom-settings-sections'),
        setSettingsSections: (csv) => localStorage.setItem('meshcom-settings-sections', csv ?? ''),
        getLoraHighlight:    () => localStorage.getItem('meshcom-lora-highlight') === '1',
        setLoraHighlight:    (v) => localStorage.setItem('meshcom-lora-highlight', v ? '1' : '0'),
        getWelcomedMinorVersion: () => localStorage.getItem('meshcom-welcomed-minor-version') || '',
        setWelcomedMinorVersion: (v) => localStorage.setItem('meshcom-welcomed-minor-version', v || ''),

        // ── Nachricht in Zwischenablage kopieren ──
        copyToClipboard: (text) => {
            // Try modern Clipboard API first (works on desktop and iOS when called
            // directly from a trusted gesture – may fail via Blazor SignalR)
            if (navigator.clipboard && navigator.clipboard.writeText) {
                return navigator.clipboard.writeText(text).catch(() => {
                    meshcomChat._copyDialog(text);
                });
            }
            meshcomChat._copyDialog(text);
            return Promise.resolve();
        },

        _copyDialog: (text) => {
            // iOS Safari: show a small modal with the text pre-selected so the user
            // can tap the native "Copy" menu. This is the only reliable method when
            // the Clipboard API is blocked (no trusted gesture via SignalR/Blazor).
            // Since iPadOS 13, Safari on iPad reports a desktop macOS user-agent
            // (no "iPad" substring) unless the user opted into the mobile site.
            // Detect it via touch support on a Mac-reported platform as well.
            var isIos = /iP(hone|ad|od)/.test(navigator.userAgent)
                || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

            if (!isIos) {
                // Desktop / Android fallback via execCommand
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.style.cssText = 'position:fixed;top:0;left:0;opacity:0;font-size:16px';
                document.body.appendChild(ta);
                ta.focus();
                ta.select();
                try { document.execCommand('copy'); } catch(e) { }
                document.body.removeChild(ta);
                return;
            }

            // iOS: show overlay dialog with pre-selected text
            var overlay = document.createElement('div');
            overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.55);z-index:9999;display:flex;align-items:center;justify-content:center';

            var box = document.createElement('div');
            box.style.cssText = 'background:#1e2535;border-radius:12px;padding:16px;width:90%;max-width:400px;box-shadow:0 4px 24px rgba(0,0,0,.5)';

            var label = document.createElement('div');
            label.textContent = 'Text markieren und kopieren:';
            label.style.cssText = 'color:#aab;font-size:13px;margin-bottom:8px';

            var input = document.createElement('input');
            input.type = 'text';
            input.value = text;
            input.readOnly = true;
            input.style.cssText = 'width:100%;box-sizing:border-box;font-size:15px;padding:8px;border-radius:6px;border:1px solid #445;background:#111827;color:#eee';

            var closeBtn = document.createElement('button');
            closeBtn.textContent = '✕ Schließen';
            closeBtn.style.cssText = 'margin-top:12px;padding:6px 16px;border-radius:6px;border:none;background:#374151;color:#eee;font-size:14px;cursor:pointer';

            box.appendChild(label);
            box.appendChild(input);
            box.appendChild(closeBtn);
            overlay.appendChild(box);
            document.body.appendChild(overlay);

            // Pre-select text so user only needs to tap "Copy"
            input.focus();
            input.setSelectionRange(0, input.value.length);

            var dismiss = () => { if (overlay.parentNode) document.body.removeChild(overlay); };
            closeBtn.addEventListener('click', dismiss);
            overlay.addEventListener('click', (e) => { if (e.target === overlay) dismiss(); });
        },

        // ── UDP-Diagnose-Popover: als position:fixed positionieren, da die Statusleiste
        // overflow:hidden hat und ein absolut positioniertes Panel sonst abgeschnitten wird.
        positionUdpDiag: (wrapEl) => {
            var panel = wrapEl && wrapEl.querySelector('.udp-diag-panel');
            if (!panel) return;
            var rect = wrapEl.getBoundingClientRect();

            var left = rect.left;
            var maxLeft = window.innerWidth - panel.offsetWidth - 8;
            if (left > maxLeft) left = Math.max(8, maxLeft);
            panel.style.left = left + 'px';

            // Bevorzugt oberhalb öffnen (Statusleiste sitzt am unteren Bildschirmrand);
            // falls kein Platz, stattdessen unterhalb öffnen.
            var top = rect.top - panel.offsetHeight - 4;
            if (top < 4) top = rect.bottom + 4;
            panel.style.top = top + 'px';
        },

        // ── SendBar: fügt Text an der aktuellen Cursorposition ein ──
        insertAtCursor: (id, text) => {
            var el = document.getElementById(id);
            if (!el) return;
            var start = el.selectionStart;
            var end   = el.selectionEnd;
            var val   = el.value;
            el.value  = val.substring(0, start) + text + val.substring(end);
            var pos   = start + text.length;
            el.setSelectionRange(pos, pos);
            el.focus();
            // Counter aktualisieren
            var bar     = el.closest('.send-bar');
            var counter = bar && bar.querySelector('.char-counter');
            if (counter) updateSendCounter(counter, el.value);
        },

        // ── SendBar: liest den aktuellen Wert des Eingabefelds ──
        getSendBarValue: (id) => {
            var el = document.getElementById(id);
            return el ? el.value : '';
        },

        // ── SendBar: setzt den Wert (z.B. nach Senden leeren oder QuickText laden) ──
        setSendBarValue: (id, value) => {
            var el = document.getElementById(id);
            if (!el) return;
            el.value = value;
            // Counter manuell aktualisieren
            var counter = el.closest('.send-bar') && el.closest('.send-bar').querySelector('.char-counter');
            if (counter) updateSendCounter(counter, value);
        },

        // ── SendBar: registriert oninput-Handler für Live-Counter ohne Blazor-Binding ──
        initSendBarCounter: (id, dotNetRef) => {            var el = document.getElementById(id);
            if (!el || el.dataset.counterInit) return;
            el.dataset.counterInit = '1';

            // Live-Counter
            el.addEventListener('input', function () {
                var bar     = el.closest('.send-bar');
                var counter = bar && bar.querySelector('.char-counter');
                if (!counter) return;
                updateSendCounter(counter, el.value);
            });

            // Keyboard: Enter = senden, Tab = Variablen expandieren – KEIN Blazor @onkeydown
            el.addEventListener('keydown', async function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    if (dotNetRef) await dotNetRef.invokeMethodAsync('JsSendAsync');
                } else if (e.key === 'Tab') {
                    e.preventDefault();
                    if (dotNetRef && el.value.includes('{')) {
                        var expanded = await dotNetRef.invokeMethodAsync('JsExpandVariables', el.value);
                        el.value = expanded;
                        // Counter aktualisieren
                        var bar     = el.closest('.send-bar');
                        var counter = bar && bar.querySelector('.char-counter');
                        if (counter) updateSendCounter(counter, expanded);
                    }
                }
            });
        },

        // ── Import/Export: JSON-Datei herunterladen ──────────────────────────────────
        downloadJson: (filename, json) => {
            var blob = new Blob([json], { type: 'application/json' });
            var url  = URL.createObjectURL(blob);
            var a    = document.createElement('a');
            a.href     = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        },

        // ── Import/Export: JSON-Datei vom User einlesen und als String zurückgeben ──
        readJsonFile: (inputId) => new Promise((resolve, reject) => {
            var input = document.getElementById(inputId);
            if (!input || !input.files || input.files.length === 0) {
                resolve(null);
                return;
            }
            var file   = input.files[0];
            var reader = new FileReader();
            reader.onload  = e  => { input.value = ''; resolve(e.target.result); };
            reader.onerror = () => { input.value = ''; reject(new Error('File read error')); };
            reader.readAsText(file, 'UTF-8');
        }),

        // ── Import/Export: Binärdaten (Uint8Array) als Datei herunterladen ──────
        downloadBinary: (filename, bytes) => {
            var blob = new Blob([new Uint8Array(bytes)], { type: 'application/octet-stream' });
            var url  = URL.createObjectURL(blob);
            var a    = document.createElement('a');
            a.href     = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        },

        // ── Import/Export: Binärdatei lesen und als Byte-Array zurückgeben ──────
        readBinaryFile: (inputId) => new Promise((resolve, reject) => {
            var input = document.getElementById(inputId);
            if (!input || !input.files || input.files.length === 0) {
                resolve(null);
                return;
            }
            var file   = input.files[0];
            var reader = new FileReader();
            reader.onload  = e => {
                input.value = '';
                var buf   = e.target.result;
                resolve(Array.from(new Uint8Array(buf)));
            };
            reader.onerror = () => { input.value = ''; reject(new Error('File read error')); };
            reader.readAsArrayBuffer(file);
        }),

        // ── Statusleiste: blendet Elemente dynamisch aus, sobald der Inhalt überläuft ──
        // Stufe 1 (.hide-compact): .status-compact ausblenden wenn scrollWidth > clientWidth
        // Stufe 2 (.hide-detail):  .status-detail  ausblenden wenn immer noch Überlauf
        // Der MutationObserver reagiert auf Datenänderungen (Texte/Badges) nachdem die
        // Seite initial gerendert wurde aber die Nutzdaten noch nicht vorlagen.
        initStatusBar: () => {
            var bar = document.querySelector('.status-bar');
            if (!bar || bar.dataset.statusBarInit) return;
            bar.dataset.statusBarInit = '1';

            var rafId = 0;
            var timerId = 0;
            var observer;

            // Prüft Überlauf zuverlässig auf Safari/iPad:
            // Overflow kurz auf visible setzen → scrollWidth zeigt echte Inhaltsbreite.
            function isOverflowing() {
                bar.style.overflow = 'visible';
                var overflowing = bar.scrollWidth > bar.offsetWidth + 4;
                bar.style.overflow = '';
                return overflowing;
            }

            function update() {
                // Observer trennen damit Klassenänderungen keinen Loop auslösen
                observer.disconnect();

                bar.classList.remove('hide-compact', 'hide-medium', 'hide-detail');

                // Stufe 1: status-compact ausblenden (IP:Port)
                if (isOverflowing()) {
                    bar.classList.add('hide-compact');
                }

                // Stufe 2: status-medium ausblenden (node-hw, GPS)
                if (isOverflowing()) {
                    bar.classList.add('hide-medium');
                }

                // Stufe 3: status-detail ausblenden (RX/TX, RSSI, Zeiten …)
                if (isOverflowing()) {
                    bar.classList.add('hide-detail');
                }

                // Observer wieder aktivieren
                observer.observe(bar, {
                    subtree: true, childList: true, characterData: true, attributes: true
                });
            }

            function scheduleUpdate() {
                if (rafId) return;
                rafId = requestAnimationFrame(function () { rafId = 0; update(); });
            }

            function scheduleUpdateDelayed() {
                if (timerId) clearTimeout(timerId);
                timerId = setTimeout(function () {
                    timerId = 0;
                    if (rafId) cancelAnimationFrame(rafId);
                    rafId = requestAnimationFrame(function () { rafId = 0; update(); });
                }, 150);
            }

            observer = new MutationObserver(scheduleUpdateDelayed);

            // Reagiert auf Größenänderung des Containers (z.B. Fenster-Resize)
            new ResizeObserver(scheduleUpdate).observe(bar);

            // Reagiert auf Textänderungen innerhalb der Statusleiste
            observer.observe(bar, {
                subtree: true, childList: true, characterData: true, attributes: true
            });

            // Erste Messung verzögert
            setTimeout(scheduleUpdate, 150);
        }
    };
}());

// ── External link confirmation ────────────────────────────────────────────
// Runs in capture phase so it fires BEFORE Blazor's event delegation.
// Reads the URL and confirm message from data attributes set by FormatMessageWithLinks().
(function () {
    document.addEventListener('click', function (e) {
        var link = e.target.closest('a.msg-url[data-ext-url]');
        if (!link) return;
        e.preventDefault();
        e.stopPropagation();
        var url = link.dataset.extUrl;
        var msg = link.dataset.extMsg || url;
        if (window.confirm(msg)) {
            window.open(url, '_blank', 'noopener,noreferrer');
        }
    }, true /* capture */);
}());

// Scroll a named element to its bottom (used by Telnet page)
window.scrollElementToBottom = function (elementId) {
    var el = document.getElementById(elementId);
    if (el) el.scrollTop = el.scrollHeight;
};

// ── CallsignPopup: Popup am Viewport-Rand ausrichten ─────────────────────
// Wird beim Anzeigen des Popups aufgerufen um sicherzustellen,
// dass es nicht außerhalb des sichtbaren Bereichs erscheint.
window.positionCallsignPopup = function (wrapEl) {
    if (!wrapEl) return;
    var card = wrapEl.querySelector('.cs-popup-card');
    if (!card) return;

    // Reset zu Standard (links-ausgerichtet)
    card.style.left   = '';
    card.style.right  = '';
    card.style.top    = '';
    card.style.bottom = '';

    var rect      = card.getBoundingClientRect();
    var vw        = window.innerWidth  || document.documentElement.clientWidth;
    var vh        = window.innerHeight || document.documentElement.clientHeight;
    var margin    = 6;

    // Horizontal: überläuft rechts?
    if (rect.right > vw - margin) {
        card.style.left  = 'auto';
        card.style.right = '0';
    }
    // Horizontal: überläuft links? (z.B. beim Pinnen)
    if (rect.left < margin) {
        card.style.left  = '0';
        card.style.right = 'auto';
    }

    // Vertikal: überläuft unten?
    if (rect.bottom > vh - margin) {
        card.style.top    = 'auto';
        card.style.bottom = '100%';
    }
};
