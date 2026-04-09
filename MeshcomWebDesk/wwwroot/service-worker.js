// Minimal service worker – enables "Add to Home Screen" / PWA installation.
// MeshCom WebDesk is a Blazor Server app and requires a live server connection;
// full offline support is not possible. This SW only provides the install trigger.

const CACHE = 'meshcom-shell-v1';
const SHELL  = ['/'];

self.addEventListener('install',  () => self.skipWaiting());
self.addEventListener('activate', () => clients.claim());

self.addEventListener('fetch', e =>
    e.respondWith(
        fetch(e.request).catch(() => caches.match(e.request))
    )
);
