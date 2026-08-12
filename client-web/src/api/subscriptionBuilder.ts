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
// Also fetches each field's own return type (kind/name) -- not just its
// name -- so a masked field (below) can be told apart from an ordinary
// scalar one before the subscription query itself gets built.
export function buildIntrospectionQuery(appId: string, eventType: string): string {
  return `query { __type(name: "${payloadTypeName(appId, eventType)}") { fields { name type { kind name } } } }`
}

export interface IntrospectedField {
  name: string
  type: { kind: string; name: string | null } | null
}

// Mirrors MaskedFieldTypes.cs's own four record names exactly -- the
// GraphQL object types FollowSubscriptionTypeModule.BuildPayloadFields
// declares (unwrapped/nullable, never NON_NULL) for any x-masking-
// annotated property, regardless of its own underlying scalar kind.
const MASKED_TYPE_NAMES = new Set(['MaskedString', 'MaskedFloat', 'MaskedBoolean', 'MaskedDateTimeOffset'])

export function isMaskedFieldType(field: IntrospectedField): boolean {
  return field.type?.kind === 'OBJECT' && field.type.name !== null && MASKED_TYPE_NAMES.has(field.type.name)
}

// A subscription field selection is either a bare field name (every
// pre-existing call site, and every ordinary scalar field) or a masked
// field needing its own { value masked erased } sub-selection -- GraphQL
// itself would otherwise reject a bare name for a composite/object-typed
// field. Found as a real, previously-unexercised gap while building the
// Vitals/Meridian proving-ground samples: no test anywhere subscribed to
// a masked field over this live path before, so this went unnoticed until
// then -- masking IS the central concern of both those domains, not an
// edge case for them.
export interface SubscriptionFieldSelector {
  name: string
  masked: boolean
}

export function toSubscriptionFieldSelectors(fields: IntrospectedField[]): SubscriptionFieldSelector[] {
  return fields.map((f) => ({ name: f.name, masked: isMaskedFieldType(f) }))
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
//
// Each entry may be a bare field name (every pre-existing caller, and any
// ordinary scalar field -- selected literally, unchanged) or a
// `SubscriptionFieldSelector` (from `toSubscriptionFieldSelectors` above)
// -- a masked one expands to its own `{ value masked erased }`
// sub-selection instead of a bare name.
// mode/fromSequenceNumber default to the pre-existing TAIL-only behavior --
// every caller before this option existed keeps producing byte-identical
// query text. REPLAY (EventTailReader's own single poll loop, server-side --
// it keeps running past its starting cursor regardless of which mode it
// started in, confirmed by reading that class directly) is what
// useEntityViewActions.ts's actual subscribe() calls now pass: a freshly-
// opened instance sees already-published history AND every subsequent live
// event through the exact same stream, closing the "waiting for the first
// event" gap a purely TAIL-only subscription can never close for data
// published before the tab connected (TODO.md's tracked gap; this is the
// concrete fix for the substance of it, not the fuller persisted-resume-
// cursor mechanism that entry's own text also describes for later).
export function buildSubscriptionQuery(
  appId: string,
  eventType: string,
  fields: Array<string | SubscriptionFieldSelector>,
  where?: ScopeFilterClause[],
  mode: 'TAIL' | 'REPLAY' = 'TAIL',
  fromSequenceNumber = 0,
): string {
  const whereArgument = where && where.length > 0 ? `, where: ${serializeWhereClauses(where)}` : ''
  const modeArgument = mode === 'REPLAY' ? `mode: REPLAY, fromSequenceNumber: ${fromSequenceNumber}` : 'mode: TAIL'
  const selection = fields.map((f) => (typeof f === 'string' ? f : f.masked ? `${f.name} { value masked erased }` : f.name)).join(' ')
  return `subscription { ${subscriptionFieldName(appId, eventType)}(${modeArgument}${whereArgument}) { ${selection} } }`
}
