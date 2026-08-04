// ADR-039's Service Worker: app-shell caching for "renders with no network
// present at all," plus the Background Sync handler for ADR-069's
// "opportunistic" outbox-flush trigger when the app isn't even open at the
// moment connectivity returns (the case src/composables/useOnlineStatus.ts
// can't cover, since nothing in that composable runs unless the app is
// already loaded). Deliberately plain, dependency-free JS with no build
// step (no vite-plugin-pwa, no bundled import from src/db/indexedDb.ts) --
// a Service Worker script is registered and executed as-is by the browser,
// so keeping it standalone avoids a second build pipeline for what is, on
// purpose, a small and rarely-changing file. The IndexedDB constants below
// are DUPLICATED from src/db/indexedDb.ts, not shared -- a real, named
// scope narrowing (see 08-build-plan.md's own Built-scope note for this
// item): if that store's shape ever changes, this file needs a matching,
// manual edit.

const APP_SHELL_CACHE = 'duplex-app-shell-v1'
const APP_SHELL_URLS = ['/', '/manifest.webmanifest']

const DB_NAME = 'duplex-client'
const DB_VERSION = 1
const OUTBOX_STORE = 'outbox'

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(APP_SHELL_CACHE).then((cache) => cache.addAll(APP_SHELL_URLS)))
})

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return // never intercept /publish, /graphql -- those must always hit the network or fail explicitly

  event.respondWith(
    fetch(event.request)
      .then((response) => {
        const copy = response.clone()
        void caches.open(APP_SHELL_CACHE).then((cache) => cache.put(event.request, copy))
        return response
      })
      .catch(() => caches.match(event.request)),
  )
})

self.addEventListener('sync', (event) => {
  if (event.tag === 'flush-outbox') event.waitUntil(flushOutbox())
})

function openDb() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION)
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })
}

function getAllPending(db) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(OUTBOX_STORE, 'readonly')
    const request = tx.objectStore(OUTBOX_STORE).getAll()
    request.onsuccess = () => resolve(request.result.filter((entry) => entry.status === 'Pending'))
    request.onerror = () => reject(request.error)
  })
}

function markDelivered(db, entry) {
  return new Promise((resolve, reject) => {
    entry.status = 'Delivered'
    const tx = db.transaction(OUTBOX_STORE, 'readwrite')
    tx.objectStore(OUTBOX_STORE).put(entry)
    tx.oncomplete = () => resolve()
    tx.onerror = () => reject(tx.error)
  })
}

// A deliberately narrower flush than src/stores/outbox.ts's own -- no
// token refresh/auth flow lives in this Service Worker (out of scope for
// this pass, see 08-build-plan.md's Built-scope note); this best-effort
// pass only delivers entries whose caller already attached a bearer token
// onto the entry itself before enqueuing, via an `authorization` field the
// ordinary Pinia-store flush path never needs (it fetches a token live).
async function flushOutbox() {
  const db = await openDb()
  const pending = await getAllPending(db)
  for (const entry of pending) {
    if (!entry.authorization) continue
    try {
      const response = await fetch(`${entry.hostBaseUrl}/publish/${entry.eventType}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: entry.authorization },
        body: JSON.stringify({
          appId: entry.appId,
          eventId: entry.commandId,
          schemaVersion: entry.schemaVersion,
          expectedVersion: entry.expectedVersion,
          payload: entry.patch,
        }),
      })
      if (response.ok) await markDelivered(db, entry)
    } catch {
      // Left Pending -- the next sync/open/focus flush retries it, same
      // fault-tolerant posture as every other outbox in this design.
    }
  }
}
