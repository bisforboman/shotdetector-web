// The page's service worker (decision 2026-10-30-threads.md). Two jobs, on every GET of the page's own origin:
// - Cross-origin isolation: GitHub Pages can't set headers, so this one adds Cross-Origin-Opener-Policy and
//   Cross-Origin-Embedder-Policy to every response it gives (the coi-serviceworker trick). The page is then
//   crossOriginIsolated, which the threaded runtime needs (SharedArrayBuffer; threads.js). Everything the page loads is
//   same-origin, so require-corp blocks nothing of it.
// - Offline: network first, the cache as the fallback (the owner's choice). Online, each request goes to the network
//   (through the browser's HTTP cache, as without a service worker) and a whole answer (200) replaces the cached copy;
//   offline, the cached copy answers. So a new deploy reaches a visitor as it always did, and the page, its bundles
//   and the demo clip (once fetched) work on a plane. A visit cut off half-way through a new deploy's files can leave a
//   mix that only the next online visit mends.
// And the installed app's share target (manifest.webmanifest, share_target; backlog "An installable app with Open with
// and a share target"): Android POSTs a shared video to share as multipart form data, which no server could take (Pages
// serves files) and none should (nothing is uploaded). This answers it: the file is kept in a cache of its own (SHARED,
// one entry, its name and modified time in headers) and the page sent to ?shared (the action's own query kept, as
// ?threads=0), where app.js takes it out and opens it as if it had been picked. A share before this worker controls the
// page (an app is installed from a page it controls) would reach the network and fail.
const CACHE = 'shotdetector-web';
const SHARED = 'shotdetector-web-shared', SHARED_FILE = 'shared-file';
const ISOLATION = { 'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp' };

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));

self.addEventListener('fetch', e => {
  const { request } = e;
  const url = new URL(request.url);
  if (request.method === 'POST' && url.origin === location.origin && url.pathname === new URL('share', location).pathname) {
    e.respondWith(shared(request, url));
    return;
  }
  if (request.method !== 'GET' || new URL(request.url).origin !== location.origin) return;
  e.respondWith(answer(request, e));
});

async function answer(request, event) {
  let response;
  try {
    response = await fetch(request);
    if (response.status === 200) {
      const copy = response.clone();
      event.waitUntil(caches.open(CACHE).then(c => c.put(request, copy)).catch(() => {}));
    }
  } catch (e) {
    // Offline: the page whatever its address says (?workers=, ?threads=), anything else as it was asked for.
    response = await caches.match(request, { ignoreSearch: request.mode === 'navigate' });
    if (!response) throw e;
  }
  return isolated(response);
}

function isolated(response) {
  const headers = new Headers(response.headers);
  for (const [name, value] of Object.entries(ISOLATION)) headers.set(name, value);
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

async function shared(request, url) {
  const to = new URL('./', location);
  to.search = url.search;
  try {
    const file = (await request.formData()).getAll('video').find(f => f instanceof File);
    if (file) {
      const cache = await caches.open(SHARED);
      await cache.put(SHARED_FILE, new Response(file, { headers: {
        'Content-Type': file.type || 'application/octet-stream',
        'X-File-Name': encodeURIComponent(file.name),
        'X-Last-Modified': String(file.lastModified),
      } }));
      to.searchParams.set('shared', '1');
    }
  } catch {
    // Not form data: the page as it is, nothing shared.
  }
  return Response.redirect(to.href, 303);
}
