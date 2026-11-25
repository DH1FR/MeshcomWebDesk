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

// Click on a desktop notification: bring the WebDesk window to the front and tell the page which tab to open.
self.addEventListener('notificationclick', e => {
  e.notification.close();
  const d = e.notification.data || {};
  e.waitUntil((async () => {
    const list = await clients.matchAll({ type: 'window', includeUncontrolled: true });
    let c = list.find(x => 'focus' in x);
    if (c) { try { await c.focus(); } catch (err) { } }
    else if (clients.openWindow) { c = await clients.openWindow('/'); }
    if (c && d.tabKey) c.postMessage({ type: 'mc-open-tab', tabKey: d.tabKey, nodeId: d.nodeId || '' });
  })());
});
