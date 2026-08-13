// ADR-039's client-local outbox/entity cache -- IndexedDB, the same
// durability bar as ADR-033's peer-sync outbox: survives a closed tab, a
// crashed browser, a restarted device. One small wrapper instead of a
// library (idb, Dexie, ...) -- the object-store shape here is tiny (two
// stores, no complex querying beyond one index), so a hand-rolled
// promise wrapper over the native API is simpler than a new dependency,
// this project's own "buy over build" principle weighed and found not to
// apply at this scale.
export const DB_NAME = 'duplex-client'
export const DB_VERSION = 2
export const OUTBOX_STORE = 'outbox'
export const ENTITY_CACHE_STORE = 'entityCache'
export const VIEW_DEFINITION_CACHE_STORE = 'viewDefinitions'
// TODO.md's resume-cursor gap -- one row per (instanceId, appId, eventType)
// subscription target, keyed by a single composite string id (IndexedDB's
// native compound-keyPath support works too, but a plain string key is
// simpler to build than a 3-tuple every call site would otherwise need to
// assemble consistently). Value is the last SequenceNumber this instance
// has durably applied for that subscription -- the server's own String-
// typed `sequenceNumber` envelope field (FollowSubscriptionTypeModule),
// parsed once here.
export const SUBSCRIPTION_CURSOR_STORE = 'subscriptionCursors'

let dbPromise: Promise<IDBDatabase> | null = null

export function openDb(): Promise<IDBDatabase> {
  if (dbPromise) return dbPromise

  dbPromise = new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, DB_VERSION)

    request.onupgradeneeded = () => {
      const db = request.result
      if (!db.objectStoreNames.contains(OUTBOX_STORE)) {
        const outbox = db.createObjectStore(OUTBOX_STORE, { keyPath: 'commandId' })
        outbox.createIndex('byInstance', 'instanceId')
      }
      if (!db.objectStoreNames.contains(ENTITY_CACHE_STORE)) {
        db.createObjectStore(ENTITY_CACHE_STORE, { keyPath: ['instanceId', 'entityId'] })
      }
      if (!db.objectStoreNames.contains(VIEW_DEFINITION_CACHE_STORE)) {
        db.createObjectStore(VIEW_DEFINITION_CACHE_STORE, { keyPath: ['entityType', 'viewKind'] })
      }
      if (!db.objectStoreNames.contains(SUBSCRIPTION_CURSOR_STORE)) {
        db.createObjectStore(SUBSCRIPTION_CURSOR_STORE, { keyPath: 'id' })
      }
    }

    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })

  return dbPromise
}

// Reset for test isolation only -- production code never needs to forget
// its own open connection.
export function resetDbConnectionForTests(): void {
  dbPromise = null
}

export function put<T>(storeName: string, value: T): Promise<void> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(storeName, 'readwrite')
        // Every caller here hands over a Pinia store entry, which Vue wraps
        // in a reactive Proxy -- IndexedDB's structured-clone algorithm
        // (correctly, per spec: a Proxy is not one of the cloneable types)
        // rejects it outright with a DataCloneError. A plain JSON round
        // trip is the simplest way to hand IndexedDB an ordinary object,
        // and every shape stored here (ClientOutboxEntry,
        // ClientEntityCacheEntry, ViewDefinitionCacheEntry) is already
        // JSON-safe by construction.
        tx.objectStore(storeName).put(JSON.parse(JSON.stringify(value)))
        tx.oncomplete = () => resolve()
        tx.onerror = () => reject(tx.error)
      }),
  )
}

export function getAll<T>(storeName: string): Promise<T[]> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(storeName, 'readonly')
        const request = tx.objectStore(storeName).getAll()
        request.onsuccess = () => resolve(request.result as T[])
        request.onerror = () => reject(request.error)
      }),
  )
}

export function getAllByIndex<T>(storeName: string, indexName: string, value: string): Promise<T[]> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(storeName, 'readonly')
        const request = tx.objectStore(storeName).index(indexName).getAll(value)
        request.onsuccess = () => resolve(request.result as T[])
        request.onerror = () => reject(request.error)
      }),
  )
}

export function get<T>(storeName: string, key: IDBValidKey): Promise<T | undefined> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(storeName, 'readonly')
        const request = tx.objectStore(storeName).get(key)
        request.onsuccess = () => resolve(request.result as T | undefined)
        request.onerror = () => reject(request.error)
      }),
  )
}

export function remove(storeName: string, key: IDBValidKey): Promise<void> {
  return openDb().then(
    (db) =>
      new Promise((resolve, reject) => {
        const tx = db.transaction(storeName, 'readwrite')
        tx.objectStore(storeName).delete(key)
        tx.oncomplete = () => resolve()
        tx.onerror = () => reject(tx.error)
      }),
  )
}
