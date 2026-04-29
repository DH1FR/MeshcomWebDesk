'use strict';

window.meshcomChat = (function () {
    const STORAGE_KEY = 'meshcom-monitor-height';

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
        getWelcomedVersion:  () => localStorage.getItem('meshcom-welcomed-version') || '',
        setWelcomedVersion:  (v) => localStorage.setItem('meshcom-welcomed-version', v || ''),
        showWelcomeDialog:   () => {
            var el = document.getElementById('welcome-dialog');
            if (el) el.style.display = 'flex';
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
            if (counter) {
                var len = value.length;
                counter.textContent = len + '/149';
                counter.className = 'char-counter' + (len >= 145 ? ' char-danger' : len >= 130 ? ' char-warn' : '');
            }
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
                var len = el.value.length;
                counter.textContent = len + '/149';
                counter.className = 'char-counter' + (len >= 145 ? ' char-danger' : len >= 130 ? ' char-warn' : '');
            });

            // Keyboard: Enter = senden, Tab = Variablen expandieren – KEIN Blazor @onkeydown
            el.addEventListener('keydown', async function (e) {
                if (e.key === 'Enter' && !e.shiftKey) {
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
                        if (counter) {
                            var len = expanded.length;
                            counter.textContent = len + '/149';
                            counter.className = 'char-counter' + (len >= 145 ? ' char-danger' : len >= 130 ? ' char-warn' : '');
                        }
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
        })
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
