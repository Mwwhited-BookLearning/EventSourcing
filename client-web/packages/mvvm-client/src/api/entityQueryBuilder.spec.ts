import { describe, expect, it } from 'vitest'
import {
  buildEntityCountQuery,
  buildEntityIntrospectionQuery,
  buildEntityListQuery,
  entityCountFieldName,
  entityGraphTypeName,
  entityListFieldName,
  selectableEntityListFieldNames,
} from './entityQueryBuilder'

describe('entityQueryBuilder', () => {
  it('builds the entity graph type name exactly matching EntityQueryTypeModule.cs (sanitized AppId, lowercased+sanitized EntityType)', () => {
    expect(entityGraphTypeName('trial1', 'Patient')).toBe('trial1_patient_Entity')
    expect(entityGraphTypeName('mvvm-demo', 'orderplaced')).toBe('mvvm_demo_orderplaced_Entity')
  })

  it('builds the entities_/entityCount_ field names matching the server-built field names exactly', () => {
    expect(entityListFieldName('trial1', 'Patient')).toBe('entities_trial1_patient')
    expect(entityCountFieldName('trial1', 'Patient')).toBe('entityCount_trial1_patient')
  })

  it('builds an introspection query against the Entity type, not the Subscription payload type', () => {
    expect(buildEntityIntrospectionQuery('trial1', 'Patient')).toBe('query { __type(name: "trial1_patient_Entity") { fields { name type { kind name } } } }')
  })

  it('excludes masked fields, the attachments list, and every reserved envelope field from the selectable set', () => {
    const fields = selectableEntityListFieldNames([
      { name: 'entityId', type: { kind: 'SCALAR', name: 'String' } },
      { name: 'isAuthoritative', type: { kind: 'SCALAR', name: 'Boolean' } },
      { name: 'authorityStatus', type: { kind: 'SCALAR', name: 'String' } },
      { name: 'version', type: { kind: 'SCALAR', name: 'Long' } },
      { name: 'schemaVersion', type: { kind: 'SCALAR', name: 'Int' } },
      { name: 'lateArrivalFlag', type: { kind: 'SCALAR', name: 'Boolean' } },
      { name: 'updatedAt', type: { kind: 'SCALAR', name: 'String' } },
      { name: 'attachments', type: { kind: 'LIST', name: null } },
      { name: 'legalName', type: { kind: 'OBJECT', name: 'MaskedString' } },
      { name: 'subjectId', type: { kind: 'SCALAR', name: 'String' } },
      { name: 'siteId', type: { kind: 'SCALAR', name: 'String' } },
    ])
    expect(fields).toEqual(['subjectId', 'siteId'])
  })

  it('builds a list query selecting entityId + authorityStatus + the given property fields, with no contains argument when omitted', () => {
    const query = buildEntityListQuery('trial1', 'Patient', ['subjectId', 'siteId'], 10, 0)
    expect(query).toBe('query { entities_trial1_patient(first: 10, skip: 0) { entityId authorityStatus subjectId siteId } }')
  })

  it('adds a JSON-escaped contains argument when a filter is given', () => {
    const query = buildEntityListQuery('trial1', 'Patient', ['subjectId'], 10, 20, 'S-009"1')
    expect(query).toBe('query { entities_trial1_patient(first: 10, skip: 20, contains: "S-009\\"1") { entityId authorityStatus subjectId } }')
  })

  it('builds a count query with no argument when unfiltered, and a contains argument when filtered', () => {
    expect(buildEntityCountQuery('trial1', 'Patient')).toBe('query { entityCount_trial1_patient }')
    expect(buildEntityCountQuery('trial1', 'Patient', 'S-0091')).toBe('query { entityCount_trial1_patient(contains: "S-0091") }')
  })
})
