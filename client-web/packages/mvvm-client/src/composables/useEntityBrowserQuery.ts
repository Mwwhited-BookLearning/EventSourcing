import { ref } from 'vue'
import { fetchToken } from '../api/authClient'
import { graphqlQuery } from '../api/graphqlClient'
import type { IntrospectedField } from '../api/subscriptionBuilder'
import { buildEntityCountQuery, buildEntityIntrospectionQuery, buildEntityListQuery, entityCountFieldName, entityListFieldName, selectableEntityListFieldNames } from '../api/entityQueryBuilder'

export interface BrowsedEntityRow {
  entityId: string
  authorityStatus: string
  data: Record<string, unknown>
}

export interface EntityBrowserQueryConfig {
  appId: string
  entityType: string
  hostBaseUrl: string
  authBaseUrl: string
  clientId: string
  clientSecret: string
  scope: string
}

// TODO.md, "Data grids: a real paged server query" -- the client half.
// EntityBrowser.vue's own data source, replacing the previous "every
// entity this instance's REPLAY subscription has ever seen, accumulated
// into useEntityCacheStore" pattern for THIS view specifically (Detail's
// own real-time cache is unaffected -- this composable is additive, not
// a replacement for that mechanism). Fetches one page at a time via the
// real entities_{appId}_{entityType}(first, skip, contains)/entityCount_
// {appId}_{entityType} GraphQL fields, so a large entity set no longer
// has to be fully streamed to this client before Browse can render a row.
export function useEntityBrowserQuery(config: EntityBrowserQueryConfig) {
  const token = ref<string>('')
  let propertyFieldsPromise: Promise<string[]> | null = null

  async function ensureToken(): Promise<string> {
    if (!token.value) token.value = await fetchToken(config.authBaseUrl, config.clientId, config.clientSecret, config.scope)
    return token.value
  }

  // Discovered once per composable instance, not once per page fetch --
  // the set of selectable fields for a given (AppId, EntityType) doesn't
  // change within one browsing session (a schema change mid-session would
  // need a page reload regardless, the same limitation useEntityViewActions'
  // own subscribe() already has for its own introspection).
  async function ensurePropertyFields(): Promise<string[]> {
    if (!propertyFieldsPromise) {
      propertyFieldsPromise = (async () => {
        const currentToken = await ensureToken()
        const introspection = await graphqlQuery<{ __type: { fields: IntrospectedField[] } | null }>(
          config.hostBaseUrl,
          currentToken,
          buildEntityIntrospectionQuery(config.appId, config.entityType),
        )
        return selectableEntityListFieldNames(introspection.__type?.fields ?? [])
      })()
    }
    return propertyFieldsPromise
  }

  async function fetchPage(pageIndex: number, pageSize: number, contains?: string): Promise<{ rows: BrowsedEntityRow[]; totalCount: number }> {
    const propertyFields = await ensurePropertyFields()
    const currentToken = await ensureToken()
    const skip = pageIndex * pageSize

    const [listResult, countResult] = await Promise.all([
      graphqlQuery<Record<string, Array<Record<string, unknown>>>>(
        config.hostBaseUrl,
        currentToken,
        buildEntityListQuery(config.appId, config.entityType, propertyFields, pageSize, skip, contains),
      ),
      graphqlQuery<Record<string, number>>(config.hostBaseUrl, currentToken, buildEntityCountQuery(config.appId, config.entityType, contains)),
    ])

    const listFieldName = entityListFieldName(config.appId, config.entityType)
    const countFieldName = entityCountFieldName(config.appId, config.entityType)
    const rawRows = listResult[listFieldName] ?? []

    const rows: BrowsedEntityRow[] = rawRows.map((row) => {
      const { entityId, authorityStatus, ...data } = row
      return { entityId: entityId as string, authorityStatus: authorityStatus as string, data }
    })

    return { rows, totalCount: countResult[countFieldName] ?? 0 }
  }

  return { fetchPage }
}
