import { defineStore } from 'pinia'
import type { ViewDefinitionCacheEntry } from '../types'
import * as clientDb from '../db/indexedDb'
import { graphqlQuery } from '../api/graphqlClient'

function keyFor(entityType: string, viewKind: string): string {
  return `${entityType}:${viewKind}`
}

interface ViewDefinitionQueryResult {
  viewDefinition: { version: number; templateContent: string } | null
}

// ADR-039's ViewDefinition registry, client side: "view definitions ... are
// cached client-side so opening an entity never requires a network round
// trip." A cache miss on first-ever load (nothing cached yet, offline) is
// indistinguishable from "no ViewDefinition exists" from EntityView's own
// point of view -- both fall back to the generic property-list view, which
// is the correct, safe default either way (never a blank/failed render).
export const useViewDefinitionsStore = defineStore('viewDefinitions', {
  state: () => ({
    entries: {} as Record<string, ViewDefinitionCacheEntry | null>,
  }),
  actions: {
    async loadFromDb() {
      const all = await clientDb.getAll<ViewDefinitionCacheEntry>(clientDb.VIEW_DEFINITION_CACHE_STORE)
      for (const entry of all) {
        this.entries[keyFor(entry.entityType, entry.viewKind)] = entry
      }
    },
    get(entityType: string, viewKind: string): ViewDefinitionCacheEntry | null | undefined {
      return this.entries[keyFor(entityType, viewKind)]
    },
    async fetchAndCache(hostBaseUrl: string, token: string, entityType: string, viewKind: string): Promise<ViewDefinitionCacheEntry | null> {
      const result = await graphqlQuery<ViewDefinitionQueryResult>(
        hostBaseUrl,
        token,
        `query($entityType: String!, $viewKind: String) { viewDefinition(entityType: $entityType, viewKind: $viewKind) { version templateContent } }`,
        { entityType, viewKind },
      )

      const key = keyFor(entityType, viewKind)
      if (!result.viewDefinition) {
        this.entries[key] = null
        return null
      }

      const entry: ViewDefinitionCacheEntry = {
        entityType,
        viewKind,
        version: result.viewDefinition.version,
        templateContent: result.viewDefinition.templateContent,
        cachedAt: new Date().toISOString(),
      }
      this.entries[key] = entry
      await clientDb.put(clientDb.VIEW_DEFINITION_CACHE_STORE, entry)
      return entry
    },
  },
})
