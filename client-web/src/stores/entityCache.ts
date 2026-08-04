import { defineStore } from 'pinia'
import type { ClientEntityCacheEntry, FollowedEventEnvelope } from '../types'
import * as clientDb from '../db/indexedDb'

function keyFor(instanceId: string, entityId: string): string {
  return `${instanceId}:${entityId}`
}

// ADR-039's client entity cache -- "last-known-good," read offline with no
// network round trip. Deliberately simpler than the server's own
// RouterWorker.FoldAsync: no ExpectedVersion/ConflictFlag COMPUTATION here
// (the server already computed conflictFlag/lateArrivalFlag and sends them
// as fixed envelope fields, ADR-024/029/038) and no Full-vs-Partial
// ChangeKind branching -- every arriving schema field simply overwrites the
// cache's own copy, latest-write-wins, since the server's own fold already
// applied whatever merge semantics its ChangeKind required before this
// client ever saw the confirmed result over its Subscription.
//
// Honest, named limitation: `extensions` always stays empty today --
// FollowSubscriptionTypeModule's dynamically-built Subscription payload
// type only ever exposes a registered schema's OWN declared properties
// (ADR-037's "a client cannot even construct a query referencing an
// undeclared field" guarantee applies here too), so an unknown property
// that landed in the server's own Extensions bag (ADR-022) never reaches
// this client at all over the current GraphQL surface. Kept as a field on
// this shape anyway, for fidelity with docs/features/mvvm-client.md's own
// ER diagram and because GenericFallbackView already renders it generically
// wherever it is populated -- not a rendering gap, a data-availability one,
// and out of this item's own three named exit criteria.
export const useEntityCacheStore = defineStore('entityCache', {
  state: () => ({
    entries: {} as Record<string, ClientEntityCacheEntry>,
  }),
  actions: {
    async loadFromDb(instanceId: string) {
      const all = await clientDb.getAll<ClientEntityCacheEntry>(clientDb.ENTITY_CACHE_STORE)
      for (const entry of all.filter((e) => e.instanceId === instanceId)) {
        this.entries[keyFor(entry.instanceId, entry.entityId)] = entry
      }
    },
    async applyFollowedEvent(instanceId: string, entityType: string, entityId: string, payload: FollowedEventEnvelope) {
      const { conflictFlag, lateArrivalFlag, authorityStatus, schemaVersion, ...schemaFields } = payload
      const key = keyFor(instanceId, entityId)
      const existing = this.entries[key]
      const entry: ClientEntityCacheEntry = {
        entityId,
        instanceId,
        entityType,
        data: { ...(existing?.data ?? {}), ...schemaFields },
        extensions: existing?.extensions ?? {},
        schemaVersion,
        conflictFlag,
        lateArrivalFlag,
        authorityStatus,
        cachedAt: new Date().toISOString(),
      }
      this.entries[key] = entry
      await clientDb.put(clientDb.ENTITY_CACHE_STORE, entry)
    },
    get(instanceId: string, entityId: string): ClientEntityCacheEntry | undefined {
      return this.entries[keyFor(instanceId, entityId)]
    },
    // ADR-065's own local-purge requirement ("Local/Edge Active-Scope
    // Caching & Erasure Invalidation," a later build-plan item) -- the
    // mechanism this store needs is already this simple removal; wiring it
    // to an EntityErasureRequested subscription event is that later item's
    // own scope, not built here.
    async purge(instanceId: string, entityId: string) {
      delete this.entries[keyFor(instanceId, entityId)]
      await clientDb.remove(clientDb.ENTITY_CACHE_STORE, [instanceId, entityId])
    },
  },
})
