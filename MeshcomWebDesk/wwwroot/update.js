// Helpers for the update page (/update). Requests carry a custom header that a foreign web page
// cannot add to a cross-origin request (CSRF protection for the local-only update endpoints).
window.meshcomUpdate = {
    isLocal: async function () {
        try {
            const r = await fetch('/api/update/local');
            return (await r.json()).local === true;
        } catch { return false; }
    },
    post: async function (path) {
        try {
            const r = await fetch(path, { method: 'POST', headers: { 'X-MeshcomWebDesk': '1' } });
            return await r.json();
        } catch (e) {
            // The process may already be restarting while the answer is on its way.
            return { ok: true, message: 'restarting' };
        }
    }
};
