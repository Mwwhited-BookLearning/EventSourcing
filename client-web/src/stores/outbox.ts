import { defineStore } from 'pinia'
import type { ClientOutboxEntry } from '../types'
import * as clientDb from '../db/indexedDb'

export type PublishFn = (entry: ClientOutboxEntry) => Promise<{ ok: boolean }>

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
  },
  actions: {
    async loadFromDb(instanceId: string) {
      this.entries = await clientDb.getAllByIndex<ClientOutboxEntry>(clientDb.OUTBOX_STORE, 'byInstance', instanceId)
    },
    async enqueue(entry: ClientOutboxEntry) {
      await clientDb.put(clientDb.OUTBOX_STORE, entry)
      this.entries.push(entry)
    },
    // Idempotent to call any number of times, from any trigger (ADR-069) --
    // a Pending entry that's already mid-flight and gets asked to flush
    // again simply gets redelivered with the SAME commandId, which the
    // server's own Idempotent Receiver (ADR-011) safely dedups.
    async flush(publish: PublishFn) {
      const pending = this.entries.filter((e) => e.status === 'Pending')
      for (const entry of pending) {
        let delivered = false
        try {
          const result = await publish(entry)
          delivered = result.ok
        } catch {
          delivered = false
        }

        if (delivered) {
          entry.status = 'Delivered'
        } else {
          entry.attempts += 1
        }
        await clientDb.put(clientDb.OUTBOX_STORE, entry)
      }
    },
  },
})
