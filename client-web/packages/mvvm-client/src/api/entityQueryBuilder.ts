import { sanitizeGraphQlName } from './subscriptionBuilder'
import type { IntrospectedField } from './subscriptionBuilder'

// TODO.md, "Data grids: a real paged server query" -- the client half.
// Mirrors subscriptionBuilder.ts's own dynamic-schema-discovery pattern
// exactly (EntityQueryTypeModule builds these fields per registered
// (AppId, EntityType) at runtime, same as FollowSubscriptionTypeModule
// does for Subscription payload types -- there is no fixed SDL for the
// client to import types from), adapted for the Entity graph type
// EntityQueryTypeModule.cs actually builds (`{appId}_{entityType}_Entity`)
// rather than a Subscription payload type.
export function entityGraphTypeName(appId: string, entityType: string): string {
  return `${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(entityType.toLowerCase())}_Entity`
}

export function entityListFieldName(appId: string, entityType: string): string {
  return `entities_${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(entityType.toLowerCase())}`
}

export function entityCountFieldName(appId: string, entityType: string): string {
  return `entityCount_${sanitizeGraphQlName(appId)}_${sanitizeGraphQlName(entityType.toLowerCase())}`
}

export function buildEntityIntrospectionQuery(appId: string, entityType: string): string {
  return `query { __type(name: "${entityGraphTypeName(appId, entityType)}") { fields { name type { kind name } } } }`
}

// Same masked-type-name set subscriptionBuilder.ts's own isMaskedFieldType
// already established -- BuildEntityPropertyFields (EntityQueryTypeModule.cs)
// uses the identical four wrapper type names for a maskable property,
// regardless of which of this file's two GraphQL type modules built it.
const MASKED_TYPE_NAMES = new Set(['MaskedString', 'MaskedFloat', 'MaskedBoolean', 'MaskedDateTimeOffset'])
const RESERVED_ENVELOPE_FIELD_NAMES = new Set(['entityId', 'isAuthoritative', 'authorityStatus', 'version', 'schemaVersion', 'lateArrivalFlag', 'updatedAt', 'attachments'])

// Deliberately excludes masked fields and the `attachments` list field, not
// an oversight: a browse-list row's own summary line has no use for a
// redacted-or-not wrapper object or a nested attachment list -- Detail
// view (the single-entity query, already unchanged) is where a caller
// reviews either. Keeping the list projection to plain, already-scalar
// property names is what lets one bare GraphQL field selection work for
// every entity type, with no per-field sub-selection logic to build here.
export function selectableEntityListFieldNames(fields: IntrospectedField[]): string[] {
  return fields
    .filter((f) => !RESERVED_ENVELOPE_FIELD_NAMES.has(f.name))
    .filter((f) => !(f.type?.kind === 'OBJECT' && f.type.name !== null && MASKED_TYPE_NAMES.has(f.type.name)))
    .map((f) => f.name)
}

export function buildEntityListQuery(appId: string, entityType: string, propertyFields: string[], first: number, skip: number, contains?: string): string {
  const selection = ['entityId', 'authorityStatus', ...propertyFields].join(' ')
  const containsArgument = contains ? `, contains: ${JSON.stringify(contains)}` : ''
  return `query { ${entityListFieldName(appId, entityType)}(first: ${first}, skip: ${skip}${containsArgument}) { ${selection} } }`
}

export function buildEntityCountQuery(appId: string, entityType: string, contains?: string): string {
  const containsArgument = contains ? `(contains: ${JSON.stringify(contains)})` : ''
  return `query { ${entityCountFieldName(appId, entityType)}${containsArgument} }`
}
