// Mirrors FollowSubscriptionTypeModule.Sanitize/BuildSubscriptionField's own
// naming convention exactly (EventStore.GraphQL, server-side) -- the client
// has no other way to know a per-event-type Subscription field's name, since
// it's built dynamically per registered event type, not from a fixed SDL.
export function sanitizeGraphQlName(value: string): string {
  return value.replace(/[^A-Za-z0-9]/g, '_')
}

// SchemaRegistryService.RegisterAsync stores EventTypeDefinition.Name
// lowercased (normalizedName = eventTypeName.ToLowerInvariant()) before
// FollowSubscriptionTypeModule ever sanitizes it -- AppId is never
// lowercased the same way, so only the event type half of a built name
// needs it here. Found as a real bug while building "Local/Edge Active-
// Scope Caching & Erasure Invalidation" (item 28): a caller passing this
// module's natural casing (e.g. "OrderPlaced", exactly what every existing
// call site already did) silently subscribed to a field name that never
// matched anything the server actually exposes.
export function payloadTypeName(appId: string, eventType: string): string {
  return `${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(eventType.toLowerCase())}_Payload`
}

export function subscriptionFieldName(appId: string, eventType: string): string {
  return `on_${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(eventType.toLowerCase())}`
}

// The client doesn't hardcode a field selection per entity type -- it asks
// the schema itself, via GraphQL's own introspection, what fields the
// dynamically-built payload type actually has, then requests all of them.
// This is what makes useEntityViewActions genuinely generic across any
// registered event type, not just a demo "Order" type wired in by name.
export function buildIntrospectionQuery(appId: string, eventType: string): string {
  return `query { __type(name: "${payloadTypeName(appId, eventType)}") { fields { name } } }`
}

// Mirrors EventStore.GraphQL.EventFilterInput's own field set exactly
// (Field/Eq/Neq/Gt/Gte/Lt/Lte/Contains, camelCased) -- ADR-065's "explicit
// scope filter" is this same FilterableFields-backed argument shape any
// GraphQL Subscription already supports (ADR-037), not a new mechanism.
export interface ScopeFilterClause {
  field: string
  eq?: string
  neq?: string
  gt?: string
  gte?: string
  lt?: string
  lte?: string
  contains?: string
}

const FILTER_CLAUSE_KEYS: ReadonlyArray<keyof ScopeFilterClause> = ['field', 'eq', 'neq', 'gt', 'gte', 'lt', 'lte', 'contains']

function serializeWhereClauses(clauses: ScopeFilterClause[]): string {
  const objects = clauses.map((clause) => {
    const assignments = FILTER_CLAUSE_KEYS.filter((key) => clause[key] !== undefined).map((key) => `${key}: ${JSON.stringify(clause[key])}`)
    return `{${assignments.join(', ')}}`
  })
  return `[${objects.join(', ')}]`
}

// `where` is omitted entirely (not sent as an empty list) when the caller
// supplies no scope filter -- an absent argument and an empty `[]` list
// aren't guaranteed equivalent to GraphQlFilterPredicateBuilder server-side,
// and every existing caller's query text should stay byte-identical to
// before this argument existed.
export function buildSubscriptionQuery(appId: string, eventType: string, fieldNames: string[], where?: ScopeFilterClause[]): string {
  const whereArgument = where && where.length > 0 ? `, where: ${serializeWhereClauses(where)}` : ''
  return `subscription { ${subscriptionFieldName(appId, eventType)}(mode: TAIL${whereArgument}) { ${fieldNames.join(' ')} } }`
}
