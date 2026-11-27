'use strict';

window.meshcomChat = (function () {
    const STORAGE_KEY = 'meshcom-monitor-height';

    // localStorage can throw (SecurityError) in Firefox when site data is blocked, with strict tracking /
    // fingerprinting protection or "delete cookies on close". Never let that break the UI: fall back to
    // an in-memory copy so toggles and the pane resizer keep working for the current page lifetime.
    var memStore = {};
    function lsGet(key) {
        try { return localStorage.getItem(key); } catch (e) { return key in memStore ? memStore[key] : null; }
    }
    function lsSet(key, value) {
        memStore[key] = String(value);
        try { localStorage.setItem(key, value); } catch (e) { }
    }
    function lsRemove(key) {
        delete memStore[key];
        try { localStorage.removeItem(key); } catch (e) { }
    }

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

    // Optional emoticon → emoji conversion while typing (Settings → Chat & Monitor). The table comes from the server
    // (EmoticonConverter.Map) via setEmoticons; null = off. Converts the whole word before the cursor when the user
    // has just typed a space/line break after it – so "http://x", ":Dank" or "8)" are never touched. The last word of
    // a message is converted on the server when it is sent.
    var emoticonMap = null;
    function convertEmoticonBeforeCursor(el, e) {
        // iOS keyboards often report the space with data=null or as insertReplacementText (autocorrect),
        // so don't rely on e.data – just check that the character left of the caret is whitespace.
        if (!emoticonMap || !e || !e.inputType || e.inputType.indexOf('insert') !== 0) return;
        var pos = el.selectionStart;
        if (pos !== el.selectionEnd || pos < 2 || !/\s/.test(el.value.charAt(pos - 1))) return;
        var before = el.value.slice(0, pos - 1);              // text up to (excluding) the space just typed
        var m = /(^|\s)(\S+)$/.exec(before);
        if (!m) return;
        var emoji = Object.prototype.hasOwnProperty.call(emoticonMap, m[2]) ? emoticonMap[m[2]] : null;
        if (!emoji) return;
        var start = before.length - m[2].length;
        el.setRangeText(emoji, start, before.length, 'preserve');
        var caret = pos - m[2].length + emoji.length;
        el.setSelectionRange(caret, caret);
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
        const saved = parseInt(lsGet(STORAGE_KEY) || '0', 10);
        if (saved > 0) {
            lowerPane.style.height = saved + 'px';
            lowerPane.style.flex   = 'none';
        }

        let startY = 0, startH = 0;

        function onMove(e) {
            if (document.documentElement.getAttribute('data-monitor') === '0') return;
            const y    = e.touches ? e.touches[0].clientY : e.clientY;
            const contH = lowerPane.parentElement.getBoundingClientRect().height;
            const newH  = Math.max(40, Math.min(contH - 80, startH + (startY - y)));
            lowerPane.style.height = newH + 'px';
            lowerPane.style.flex   = 'none';
            if (e.cancelable) e.preventDefault();
        }

        function onUp() {
            var h = Math.round(lowerPane.getBoundingClientRect().height);
            lsSet(STORAGE_KEY, h);
            if (window.meshcomMonitor) window.meshcomMonitor.setHeight(h);
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup',   onUp);
            document.removeEventListener('touchmove', onMove);
            document.removeEventListener('touchend',  onUp);
        }

        function onDown(e) {
            if (document.documentElement.getAttribute('data-monitor') === '0') return;
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

        // Many open tabs: wrapped onto several rows they eat the chat area. When that happens (on any screen size,
        // not only on phones) switch to the one-row swipeable layout (.tabs-scroll, same rules as on phones).
        // The wrapped height is measured with the class removed, so the decision cannot flip-flop.
        var fitQueued = false;
        function fitTabs() {
            fitQueued = false;
            var pane = tabBar.closest('.upper-pane');
            if (!pane) return;
            var had = tabBar.classList.contains('tabs-scroll');
            if (had) tabBar.classList.remove('tabs-scroll');
            var barH  = tabBar.offsetHeight;
            var paneH = pane.clientHeight;
            var scroll = barH > 40 && (paneH - barH < 220 || barH > paneH * 0.3);
            if (scroll) tabBar.classList.add('tabs-scroll');
            if (scroll !== had && scroll && window.meshcomChat) window.meshcomChat.revealActiveTab();
        }
        function queueFit() {
            if (fitQueued) return;
            fitQueued = true;
            requestAnimationFrame(fitTabs);
        }
        new MutationObserver(queueFit).observe(tabBar, { childList: true });
        if (window.ResizeObserver && tabBar.closest('.upper-pane')) new ResizeObserver(queueFit).observe(tabBar.closest('.upper-pane'));
        window.addEventListener('resize', queueFit);
        queueFit();

        // Narrow screens (one swipeable row): bring a tapped tab to the middle right away, without waiting
        // for the server round trip that re-renders the bar.
        tabBar.addEventListener('click', function (e) {
            if (e.target.closest('.tab-close, .tab-ai-icon')) return;
            var btn = e.target.closest('.tab-button[data-tab-key]');
            if (!btn || tabBar.scrollWidth <= tabBar.clientWidth + 1) return;
            tabBar.scrollTo({ left: btn.offsetLeft - (tabBar.clientWidth - btn.offsetWidth) / 2, behavior: 'smooth' });
        });

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

    // ── Attention tracking ────────────────────────────────────────────────
    // Tells the server whether anyone is really looking at this page: the browser tab must be
    // visible and there must have been mouse/keyboard/touch activity recently. The server
    // extrapolates the idle time between reports, so only changes and activity are sent.
    var attention = null;

    function attentionStart(dotNetRef) {
        attentionStop();
        var lastActivity = performance.now();
        var lastSent     = 0;
        var timer        = null;

        function send(force) {
            var now = performance.now();
            if (!force && now - lastSent < 4000) return;
            lastSent = now;
            // "Looking" needs a visible browser tab AND a focused window: a window in the
            // background (another application in front) is not being looked at.
            var looking = document.visibilityState === 'visible' && document.hasFocus();
            dotNetRef.invokeMethodAsync('ReportAttention', looking, now - lastActivity)
                .catch(function () { });
        }
        function onActivity(e) {
            // Script-dispatched events are not the user.
            if (e && e.isTrusted === false) return;
            lastActivity = performance.now();
            send(false);
        }
        // Browsers synthesise mousemove events when content moves under a resting cursor
        // (e.g. the chat auto-scrolling on a new message). Only a real change of the screen
        // position counts. Plain 'scroll' events are not used at all for the same reason –
        // genuine scrolling always comes with wheel, touch, key or mouse input.
        var lastX = null, lastY = null;
        function onMouseMove(e) {
            if (e.screenX === lastX && e.screenY === lastY) return;
            lastX = e.screenX; lastY = e.screenY;
            onActivity(e);
        }
        function onFocusChange() { send(true); }
        function onVisibility() {
            if (document.visibilityState === 'visible') lastActivity = performance.now();
            send(true);
        }

        var activityEvents = ['mousedown', 'keydown', 'touchstart', 'wheel'];
        activityEvents.forEach(function (n) {
            window.addEventListener(n, onActivity, { passive: true, capture: true });
        });
        window.addEventListener('mousemove', onMouseMove, { passive: true, capture: true });
        document.addEventListener('visibilitychange', onVisibility);
        window.addEventListener('focus', onActivity);
        window.addEventListener('blur', onFocusChange);

        // Safety net: a slow heartbeat while visible, so the server never relies on an old report.
        timer = setInterval(function () { send(true); }, 20000);

        attention = {
            stop: function () {
                activityEvents.forEach(function (n) {
                    window.removeEventListener(n, onActivity, { capture: true });
                });
                window.removeEventListener('mousemove', onMouseMove, { capture: true });
                document.removeEventListener('visibilitychange', onVisibility);
                window.removeEventListener('focus', onActivity);
                window.removeEventListener('blur', onFocusChange);
                clearInterval(timer);
            }
        };
        send(true);
    }

    function attentionStop() {
        if (attention) { attention.stop(); attention = null; }
    }

    // ── Unread badge in the browser tab / on the installed app's icon ─────
    // n   = all unread messages, imp = the part of them that is for us (direct messages + mentions).
    // Title: "(n) " or "(n · imp💬) "; favicon: red dot (unread) + orange dot (direct/mention);
    // app icon badge (taskbar / home screen): imp only – the Badging API can show a single number.
    // Blazor's <PageTitle> does not set the number; this applies it and re-applies it if something overwrote it.
    var favImg = null, favLink = null, favLastKey = '';

    function setTabBadge(n, imp) {
        n   = n > 0 ? n : 0;
        imp = n > 0 && imp > 0 ? Math.min(imp, n) : 0;
        function fmt(v) { return v > 99 ? '99+' : String(v); }
        try {
            var baseTitle = document.title.replace(/^\(\d+\+?(?: · \d+\+?💬)?\) /, '');
            var wanted = n > 0 ? '(' + fmt(n) + (imp > 0 ? ' · ' + fmt(imp) + '💬' : '') + ') ' + baseTitle : baseTitle;
            if (document.title !== wanted) document.title = wanted;
        } catch (e) { }
        try {
            if (imp > 0 && navigator.setAppBadge) navigator.setAppBadge(imp).catch(function () { });
            else if (imp === 0 && navigator.clearAppBadge) navigator.clearAppBadge().catch(function () { });
        } catch (e) { }
        var key = n + '|' + (imp > 0 ? 1 : 0);
        if (key === favLastKey) return;
        favLastKey = key;
        try {
            if (!favLink) {
                favLink = document.createElement('link');
                favLink.rel = 'icon';
                favLink.id  = 'mc-unread-favicon';
                favLink.type = 'image/png';
            }
            if (n === 0) {
                if (favLink.parentNode) favLink.parentNode.removeChild(favLink);
                return;
            }
            function draw() {
                var c = document.createElement('canvas');
                c.width = c.height = 64;
                var g = c.getContext('2d');
                if (favImg && favImg.complete && favImg.naturalWidth) g.drawImage(favImg, 0, 0, 64, 64);
                function dot(x, y, color) {
                    g.beginPath(); g.arc(x, y, 16, 0, Math.PI * 2);
                    g.fillStyle = color; g.fill();
                    g.lineWidth = 4; g.strokeStyle = '#ffffff'; g.stroke();
                }
                dot(46, 18, '#e53935');                  // unread
                if (favLastKey.slice(-1) === '1') dot(46, 46, '#fb8c00');   // direct message / mention
                favLink.href = c.toDataURL('image/png');
                if (!favLink.parentNode) document.head.appendChild(favLink);
            }
            if (!favImg) {
                favImg = new Image();
                favImg.onload  = function () { if (favLastKey !== '0|0') draw(); };
                favImg.onerror = function () { if (favLastKey !== '0|0') draw(); };
                favImg.src = 'favicon.png';
            } else {
                draw();
            }
        } catch (e) { /* cosmetic only */ }
    }

    // ── Desktop notifications (browser Notification API) ───────────────────
    var notifyRef = null;

    function getNotificationPermission() {
        return ('Notification' in window) ? Notification.permission : 'unsupported';
    }

    function requestNotificationPermission() {
        if (!('Notification' in window)) return Promise.resolve('unsupported');
        if (Notification.permission !== 'default') return Promise.resolve(Notification.permission);
        try {
            return Promise.resolve(Notification.requestPermission()).then(function (r) { return r || Notification.permission; });
        } catch (e) { return Promise.resolve(Notification.permission); }
    }

    // Chrome on Android does not allow `new Notification()` – there it only works through the service worker.
    // So the service worker is preferred everywhere; the page constructor is the fallback. A click is handled by
    // the service worker (focus the window) which then tells the page which tab to open.
    function setNotifyHandler(dotNetRef) {
        notifyRef = dotNetRef;
        if (!('serviceWorker' in navigator) || window.__mcNotifyMsg) return;
        window.__mcNotifyMsg = true;
        navigator.serviceWorker.addEventListener('message', function (ev) {
            var d = ev.data;
            if (d && d.type === 'mc-open-tab' && notifyRef && d.tabKey)
                notifyRef.invokeMethodAsync('OpenTabFromNotification', d.tabKey, d.nodeId || '').catch(function () { });
        });
    }

    // Same tag = replaces an older pop-up (also across several open WebDesk tabs) instead of piling up.
    function showNotification(title, body, tag, silent, tabKey, nodeId) {
        if (!('Notification' in window) || Notification.permission !== 'granted') return false;
        var opts = {
            body: body, tag: tag || undefined, renotify: !!tag, silent: !!silent, icon: 'icons/notification-icon.png',
            data: { tabKey: tabKey || '', nodeId: nodeId || '' }
        };
        function viaPage() {
            try {
                var n = new Notification(title, opts);
                n.onclick = function () {
                    try { window.focus(); } catch (e) { }
                    n.close();
                    if (notifyRef && tabKey) notifyRef.invokeMethodAsync('OpenTabFromNotification', tabKey, nodeId || '').catch(function () { });
                };
                setTimeout(function () { try { n.close(); } catch (e) { } }, 20000);
                return true;
            } catch (e) { return false; }
        }
        if ('serviceWorker' in navigator && navigator.serviceWorker.getRegistration) {
            return navigator.serviceWorker.getRegistration().then(function (reg) {
                if (reg && reg.showNotification) return reg.showNotification(title, opts).then(function () { return true; });
                return viaPage();
            }).catch(function () { return viaPage(); });
        }
        return viaPage();
    }

    return {
        initResizer,
        initTabDrag,
        setTabBadge,
        getNotificationPermission,
        requestNotificationPermission,
        setNotifyHandler,
        showNotification,
        initQuickTextDrag,
        attentionStart,
        attentionStop,
        // Narrow screens show the tabs in one swipeable row: bring the active tab into view.
        revealActiveTab: () => {
            var bar = document.querySelector('.tab-bar');
            var btn = bar && bar.querySelector('.tab-button.active');
            if (!bar || !btn || bar.scrollWidth <= bar.clientWidth + 1) return;
            bar.scrollTo({ left: btn.offsetLeft - (bar.clientWidth - btn.offsetWidth) / 2, behavior: 'smooth' });
        },
        getActiveTab:      () => lsGet('meshcom-active-tab') || '',
        setActiveTab:      (key) => {
            if (key) lsSet('meshcom-active-tab', key);
            else     lsRemove('meshcom-active-tab');
        },
        getMonitorVisible: () => {
            var v = lsGet('meshcom-monitor-visible-v2');
            // First visit on a compact (tiny) display: start with the monitor hidden
            if (v === null && document.documentElement.getAttribute('data-display') === 'compact') return false;
            return v === null ? null : v === '1';
        },
        setMonitorVisible: (visible) => {
            lsSet('meshcom-monitor-visible-v2', visible ? '1' : '0');
            if (window.meshcomMonitor) window.meshcomMonitor.setVisible(visible, true);
        },
        getVoiceEnabled: () => {
            var v = lsGet('meshcom-voice-enabled');
            return v === null ? null : v === '1';
        },
        setVoiceEnabled: (enabled) =>
            lsSet('meshcom-voice-enabled', enabled ? '1' : '0'),
        getTabOrder: () => {
            var v = lsGet('meshcom-tab-order');
            try { return v ? JSON.parse(v) : []; } catch (e) { return []; }
        },
        setTabOrder: (keys) =>
            lsSet('meshcom-tab-order', JSON.stringify(keys || [])),
        getSetupHintDismissed: () => { try { return localStorage.getItem('meshcom-setup-hint-dismissed') === '1'; } catch (e) { return false; } },
        setSetupHintDismissed: () => { try { localStorage.setItem('meshcom-setup-hint-dismissed', '1'); } catch (e) { } },
        getSetupPromptSeen: () => { try { return sessionStorage.getItem('meshcom-setup-prompt-seen') === '1'; } catch (e) { return false; } },
        setSetupPromptSeen: () => { try { sessionStorage.setItem('meshcom-setup-prompt-seen', '1'); } catch (e) { } },
        scrollToId: (id) => { const el = document.getElementById(id); if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' }); },
        getSettingsSections: () => lsGet('meshcom-settings-sections'),
        setSettingsSections: (csv) => lsSet('meshcom-settings-sections', csv ?? ''),
        getLoraHighlight:    () => lsGet('meshcom-lora-highlight') === '1',
        setLoraHighlight:    (v) => lsSet('meshcom-lora-highlight', v ? '1' : '0'),
        getWelcomedMinorVersion: () => lsGet('meshcom-welcomed-minor-version') || '',
        setWelcomedMinorVersion: (v) => lsSet('meshcom-welcomed-minor-version', v || ''),

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

        // ── SendBar: fügt einen QuickText an der Cursorposition ein (mit Leerzeichen davor/danach, falls nötig) ──
        insertQuickTextAtCursor: (id, text, maxLength) => {
            var el = document.getElementById(id);
            if (!el) return;
            var start = el.selectionStart;
            var end   = el.selectionEnd;
            var val   = el.value;

            var prefix = (start > 0 && !/\s/.test(val.charAt(start - 1))) ? ' ' : '';
            var suffix = (end < val.length && !/\s/.test(val.charAt(end))) ? ' ' : '';
            var insertText = prefix + text + suffix;

            var newVal = val.substring(0, start) + insertText + val.substring(end);
            var pos    = start + insertText.length;
            if (newVal.length > maxLength) {
                newVal = newVal.substring(0, maxLength);
                pos    = Math.min(pos, newVal.length);
            }

            el.value = newVal;
            el.setSelectionRange(pos, pos);
            el.focus();
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
        setEmoticons: (map) => { emoticonMap = map || null; },
        initSendBarCounter: (id, dotNetRef) => {            var el = document.getElementById(id);
            if (!el || el.dataset.counterInit) return;
            el.dataset.counterInit = '1';

            // Live-Counter (+ optional emoticon → emoji conversion, see setEmoticons)
            el.addEventListener('input', function (e) {
                convertEmoticonBeforeCursor(el, e);
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

// ── Chat message list: follow the end only while the reader is at the end ──
// 'stick' = the list was at (or near) the bottom before the last render. New content does not
// change scrollTop, so no scroll event fires and the value still describes the pre-render state.
window.chatScroll = (function () {
    var stick = true;
    var dn = null;
    function atBottom(el) { return el.scrollHeight - el.scrollTop - el.clientHeight < 40; }

    // Only the reader may switch following off. The browser also fires scroll events on its own when rows are
    // removed at the top (full buffer) or content is re-laid out; at that moment freshly appended rows are not yet
    // scrolled into view, which would look like "not at the end" and wrongly stop following.
    var userUntil = 0;
    var dragging  = false;
    function inList(e) { var t = e.target; return !!(t && t.closest && t.closest('#tab-messages')); }
    function markUser(e) { if (inList(e)) userUntil = performance.now() + 800; }
    ['wheel', 'touchstart', 'touchmove', 'keydown'].forEach(function (n) {
        document.addEventListener(n, markUser, { passive: true, capture: true });
    });
    document.addEventListener('mousedown', function (e) { if (inList(e)) { dragging = true; userUntil = performance.now() + 800; } }, true);
    document.addEventListener('mouseup', function () { dragging = false; }, true);

    // scroll does not bubble → capture on document, works for a re-created #tab-messages as well
    document.addEventListener('scroll', function (e) {
        var el = e.target;
        if (!el || el.id !== 'tab-messages') return;
        var now = atBottom(el);
        if (!now && !dragging && performance.now() > userUntil) return;   // layout-driven, not the reader
        if (now !== stick) {
            stick = now;
            if (dn) dn.invokeMethodAsync('OnChatStickChanged', now).catch(function () { });
        }
    }, true);

    return {
        init: function (ref) { dn = ref; },
        // Scrolls to the bottom if forced or if the reader was at the bottom; returns whether it scrolled.
        after: function (id, force) {
            var el = document.getElementById(id);
            if (!el) return false;
            if (force || stick) {
                el.scrollTop = el.scrollHeight;
                stick = true;
                return true;
            }
            return false;
        }
    };
}());

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
