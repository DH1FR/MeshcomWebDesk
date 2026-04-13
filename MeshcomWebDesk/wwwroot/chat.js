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

    return {
        initResizer,
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
        getSettingsSections: () => localStorage.getItem('meshcom-settings-sections'),
        setSettingsSections: (csv) => localStorage.setItem('meshcom-settings-sections', csv ?? '')
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
