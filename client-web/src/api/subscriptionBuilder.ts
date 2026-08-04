// Mirrors FollowSubscriptionTypeModule.Sanitize/BuildSubscriptionField's own
// naming convention exactly (EventStore.GraphQL, server-side) -- the client
// has no other way to know a per-event-type Subscription field's name, since
// it's built dynamically per registered event type, not from a fixed SDL.
export function sanitizeGraphQlName(value: string): string {
  return value.replace(/[^A-Za-z0-9]/g, '_')
}

export function payloadTypeName(appId: string, eventType: string): string {
  return `${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(eventType)}_Payload`
}

export function subscriptionFieldName(appId: string, eventType: string): string {
  return `on_${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(eventType)}`
}

// The client doesn't hardcode a field selection per entity type -- it asks
// the schema itself, via GraphQL's own introspection, what fields the
// dynamically-built payload type actually has, then requests all of them.
// This is what makes useEntityViewActions genuinely generic across any
// registered event type, not just a demo "Order" type wired in by name.
export function buildIntrospectionQuery(appId: string, eventType: string): string {
  return `query { __type(name: "${payloadTypeName(appId, eventType)}") { fields { name } } }`
}

export function buildSubscriptionQuery(appId: string, eventType: string, fieldNames: string[]): string {
  return `subscription { ${subscriptionFieldName(appId, eventType)}(mode: TAIL) { ${fieldNames.join(' ')} } }`
}
