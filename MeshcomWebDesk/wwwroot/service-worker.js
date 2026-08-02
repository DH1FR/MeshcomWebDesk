// Minimal service worker – enables "Add to Home Screen" / PWA installation.
// MeshCom WebDesk is a Blazor Server app and requires a live server connection;
// full offline support is not possible. This SW only provides the install trigger.

self.addEventListener('install',  () => self.skipWaiting());
self.addEventListener('activate', () => clients.claim());

// Nothing is ever cached (no offline support), so just pass requests through to the
// network. Falling back to caches.match() here would resolve to undefined and make
// respondWith() throw "Failed to convert value to 'Response'" whenever a fetch fails
// transiently (e.g. the network adapter not being back up yet right after the OS wakes
// from sleep) - which broke reloading the page until a hard refresh forced Chrome to
// bypass this service worker.
self.addEventListener('fetch', e => e.respondWith(fetch(e.request)));
