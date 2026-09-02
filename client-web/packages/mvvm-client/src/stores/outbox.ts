import { defineStore } from 'pinia'
import type { ClientOutboxEntry } from '../types'
import * as clientDb from '../db/indexedDb'
import { exportOutboxBundle, importOutboxBundle } from '../outbox/exportImport'

export type PublishFn = (entry: ClientOutboxEntry) => Promise<{ ok: boolean; permanentFailure?: boolean }>
export type IngestSampleFn = (entry: ClientOutboxEntry) => Promise<{ ok: boolean }>

// ADR-069's "opportunistic" category, armed: registering the SAME sync
// tag public/sw.js's own `sync` event listener already handles -- a real,
// pre-existing gap found while building this item (the listener existed,
// nothing ever called `.register()`, docs/patterns/pwa-offline-outbox.md's
// own sequence diagram already specified this exact call). Feature-
// detected, not assumed (`SyncManager` is absent on Firefox/Safari, the
// same caveat every other Background Sync mention in this codebase
// already states) -- silently skipped, never thrown, when unsupported;
// `useOnlineStatus`'s open-focus fallback still covers that case.
//
// A second, previously-undiscovered failure mode, found only by actually
// driving `enqueue` through a real Playwright/Chromium browser (never
// exercised for real before -- every prior test was a mocked Vitest
// spec): `sync.register()` can genuinely THROW even where `SyncManager`
// itself is present (a real `NotAllowedError` in an automated/headless
// context with no Background Sync permission grant, e.g. under
// `BrowserContext.SetOfflineAsync`). The feature-detect above only
// covers the API being absent, not a runtime rejection -- and this is a
// best-effort, opportunistic optimization layered on top of the outbox
// write, never something the outbox's own durability guarantee
// (CLAUDE.md's standing "never lose or corrupt data" requirement) may
// depend on succeeding. `enqueue`'s caller (`useEntityViewActions.
// dispatchCommand`/`captureDeviceReading`) must see the entry safely
// queued either way, so this is wrapped rather than left to propagate.
async function registerBackgroundSync(): Promise<void> {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return
  try {
    const registration = await navigator.serviceWorker.ready
    if (!('sync' in registration)) return
    await (registration as ServiceWorkerRegistration & { sync: { register(tag: string): Promise<void> } }).sync.register('flush-outbox')
  } catch {
    // Best-effort only -- useOnlineStatus's open-focus fallback and every
    // other flush trigger (ADR-069) still cover delivery regardless.
  }
}

// ADR-039's client-local outbox (Model layer, per the Vue mapping in
// docs/patterns/mvvm-client-architecture.md): commands enqueue here, NEVER
// mutate ClientEntityCacheEntry directly -- the "real" state change only
// lands once the round trip through the server's Entity Store confirms it
// (useEntityViewActions.dispatchCommand is the only caller that enqueues;
// the entity cache store is updated exclusively by a confirmed Subscription
// event flowing back, never by this store).
export const useOutboxStore = defineStore('outbox', {
  state: () => ({
    entries: [] as ClientOutboxEntry[],
  }),
  getters: {
    pendingFor: (state) => (instanceId: string) => state.entries.filter((e) => e.instanceId === instanceId && e.status === 'Pending'),
    // A permanently-failed entry (flush's own new `permanentFailure`
    // handling below) is terminal, not pending -- it must still be
    // visible somewhere, or it would simply vanish from the UI's queued
    // count with no signal anything ever went wrong.
    failedFor: (state) => (instanceId: string) => state.entries.filter((e) => e.instanceId === instanceId && e.status === 'Failed'),
  },
  actions: {
    async loadFromDb(instanceId: string) {
      this.entries = await clientDb.getAllByIndex<ClientOutboxEntry>(clientDb.OUTBOX_STORE, 'byInstance', instanceId)
    },
    async enqueue(entry: ClientOutboxEntry) {
      await clientDb.put(clientDb.OUTBOX_STORE, entry)
      this.entries.push(entry)
      await registerBackgroundSync()
    },
    // ADR-069's explicit/manual "sneakernet" transfer, for a device with
    // no network path at all -- reuses ADR-068's portable bundle shape
    // (client-web/src/outbox/bundle.ts), never a second, bespoke format.
    async exportBundle(instanceId: string): Promise<string> {
      return exportOutboxBundle(this.pendingFor(instanceId), instanceId)
    },
    // Verifies the bundle before enqueueing anything -- a tampered or
    // truncated bundle imports nothing. An entry whose commandId already
    // exists locally is skipped, not re-enqueued as a duplicate --
    // ADR-011's idempotency already makes a redundant enqueue-and-flush
    // safe at the server, but skipping it here avoids a pointless second
    // local IndexedDB row for the exact same command.
    async importBundle(ndjson: string): Promise<{ verified: boolean; importedCount: number }> {
      const result = await importOutboxBundle(ndjson)
      if (!result.verified) return { verified: false, importedCount: 0 }

      const existingIds = new Set(this.entries.map((e) => e.commandId))
      const toImport = result.entries.filter((e) => !existingIds.has(e.commandId))
      for (const entry of toImport) {
        await clientDb.put(clientDb.OUTBOX_STORE, entry)
        this.entries.push(entry)
      }
      return { verified: true, importedCount: toImport.length }
    },
    // Idempotent to call any number of times, from any trigger (ADR-069) --
    // a Pending entry that's already mid-flight and gets asked to flush
    // again simply gets redelivered with the SAME commandId, which the
    // server's own Idempotent Receiver (ADR-011) safely dedups.
    // `ingestSample` is optional and trailing (the same low-invasiveness
    // convention this repo's own server-side constructors already use for
    // an additive dependency) -- every pre-item-44 call site passing only
    // `publish` is completely unaffected. A 'streamingSample' entry
    // flushed with no `ingestSample` supplied is left Pending rather than
    // silently dropped or misrouted to `publish` -- the same fail-safe
    // "retry on the next flush" posture every other delivery failure here
    // already has.
    async flush(publish: PublishFn, ingestSample?: IngestSampleFn) {
      const pending = this.entries.filter((e) => e.status === 'Pending')
      for (const entry of pending) {
        if (entry.deliveryKind === 'streamingSample' && !ingestSample) continue

        let delivered = false
        let permanentFailure = false
        try {
          const result = entry.deliveryKind === 'streamingSample' ? await ingestSample!(entry) : await publish(entry)
          delivered = result.ok
          permanentFailure = !delivered && 'permanentFailure' in result && result.permanentFailure === true
        } catch {
          // A thrown exception (network drop, DNS failure, ...) is always
          // transient -- only a real HTTP response the server actually
          // returned can be a permanent, never-retry-again rejection.
          delivered = false
        }

        if (delivered) {
          entry.status = 'Delivered'
        } else if (permanentFailure) {
          // A 400/403 the server will never accept by mere retry --
          // terminal, distinct from an ordinary transient failure. Without
          // this, the outbox retried a genuinely, permanently rejected
          // command forever, silently, with no visible signal anything was
          // even wrong -- the real damage behind TODO.md's own "Dispatch a
          // command demo panel" gap, found while fixing that gap for real.
          entry.status = 'Failed'
        } else {
          entry.attempts += 1
        }
        await clientDb.put(clientDb.OUTBOX_STORE, entry)
      }
    },
  },
})
