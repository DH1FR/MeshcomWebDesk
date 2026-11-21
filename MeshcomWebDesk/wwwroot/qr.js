// Renders a QR code (SVG) into the element with the given id. Uses lib/qrcode/qrcode.js (qrcode-generator, MIT).
window.meshcomQr = {
    render: function (id, text) {
        var el = document.getElementById(id);
        if (!el || typeof qrcode !== 'function') return false;
        try {
            var qr = qrcode(0, 'M');          // type 0 = pick the smallest version that fits, error correction M
            qr.addData(text);
            qr.make();
            el.innerHTML = qr.createSvgTag({ cellSize: 6, margin: 0, scalable: true });
            var svg = el.querySelector('svg');
            if (svg) { svg.setAttribute('role', 'img'); svg.setAttribute('aria-label', 'QR code'); }
            return true;
        } catch (e) {
            el.textContent = '';
            return false;
        }
    }
};
