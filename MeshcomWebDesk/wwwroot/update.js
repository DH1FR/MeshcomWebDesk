// Helpers for the update page (/update). Requests carry a custom header that a foreign web page
// cannot add to a cross-origin request (CSRF protection for the local-only update endpoints).
window.meshcomUpdate = {
    isLocal: async function () {
        try {
            const r = await fetch('/api/update/local');
            return (await r.json()).local === true;
        } catch { return false; }
    },
    // After a Docker update was requested: poll until the (new) container answers with another version/commit.
    watchDocker: function (version, commit) {
        const started = Date.now();
        const timer = setInterval(async function () {
            if (Date.now() - started > 10 * 60 * 1000) { clearInterval(timer); return; }
            try {
                const r = await fetch('/api/update/docker/info', { cache: 'no-store' });
                if (!r.ok) return;
                const j = await r.json();
                if (j.version !== version || (j.runningCommit || '') !== (commit || '')) {
                    clearInterval(timer);
                    location.reload();
                }
            } catch (e) { /* the container is being replaced */ }
        }, 4000);
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
