import { describe, expect, it } from 'vitest'
import {
  buildIntrospectionQuery,
  buildSubscriptionQuery,
  isMaskedFieldType,
  payloadTypeName,
  sanitizeGraphQlName,
  subscriptionFieldName,
  toSubscriptionFieldSelectors,
} from './subscriptionBuilder'

describe('subscriptionBuilder (mirrors FollowSubscriptionTypeModule.Sanitize/Name-normalization exactly)', () => {
  it('sanitizes non-alphanumeric characters the same way the server does', () => {
    expect(sanitizeGraphQlName('mvvm-demo-1')).toBe('mvvm_demo_1')
    // sanitizeGraphQlName itself never lowercases -- it's shared by AppId
    // (never lowercased server-side) and, via the two functions below, the
    // event type (which IS lowercased, but by them, not by this function).
    expect(sanitizeGraphQlName('OrderPlaced')).toBe('OrderPlaced')
  })

  it('builds the payload type name in AppId_name_Payload form, lowercasing only the event type to match EventTypeDefinition.Name', () => {
    expect(payloadTypeName('mvvm-demo-1', 'OrderPlaced')).toBe('mvvm_demo_1_orderplaced_Payload')
  })

  it('builds the subscription field name in on_AppId_name form, lowercasing only the event type', () => {
    expect(subscriptionFieldName('mvvm-demo-1', 'OrderPlaced')).toBe('on_mvvm_demo_1_orderplaced')
  })

  it('builds an introspection query naming the exact dynamically-built payload type, requesting each field\'s own return type', () => {
    const query = buildIntrospectionQuery('mvvm-demo-1', 'OrderPlaced')
    expect(query).toContain('__type(name: "mvvm_demo_1_orderplaced_Payload")')
    expect(query).toContain('type { kind name }')
  })

  it('builds a subscription query requesting every introspected field, with no where argument when no scope filter is supplied', () => {
    const query = buildSubscriptionQuery('mvvm-demo-1', 'OrderPlaced', ['orderId', 'amount', 'conflictFlag'])
    expect(query).toBe('subscription { on_mvvm_demo_1_orderplaced(mode: TAIL) { orderId amount conflictFlag } }')
  })

  it('builds a subscription query carrying an explicit scope filter (ADR-065), the same [EventFilterInput!] shape the server exposes', () => {
    const query = buildSubscriptionQuery('mvvm-demo-1', 'OrderPlaced', ['orderId', 'status'], [{ field: 'Status', eq: 'open' }])
    expect(query).toBe('subscription { on_mvvm_demo_1_orderplaced(mode: TAIL, where: [{field: "Status", eq: "open"}]) { orderId status } }')
  })

  it('serializes multiple where clauses and multiple operators on one clause', () => {
    const query = buildSubscriptionQuery('mvvm-demo-1', 'OrderPlaced', ['orderId'], [
      { field: 'Status', eq: 'open' },
      { field: 'AssignedSite', eq: 'site-1', neq: 'site-2' },
    ])
    expect(query).toBe(
      'subscription { on_mvvm_demo_1_orderplaced(mode: TAIL, where: [{field: "Status", eq: "open"}, {field: "AssignedSite", eq: "site-1", neq: "site-2"}]) { orderId } }',
    )
  })

  // A masked field (x-masking, ADR-009) resolves server-side to one of the
  // four MaskedFieldTypes.cs records (MaskedString/Float/Boolean/
  // DateTimeOffset) -- an OBJECT-kind GraphQL type, never a bare scalar --
  // so a bare field name in the subscription selection would be invalid
  // GraphQL for it. Found as a real, previously-unexercised gap while
  // building the Vitals/Meridian proving-ground samples: no test anywhere
  // had ever subscribed to a masked field over this live path before.
  describe('masked fields (ADR-009)', () => {
    it('identifies each of the four MaskedFieldTypes.cs record names as a masked field, and an ordinary scalar type as not', () => {
      expect(isMaskedFieldType({ name: 'legalName', type: { kind: 'OBJECT', name: 'MaskedString' } })).toBe(true)
      expect(isMaskedFieldType({ name: 'confidence', type: { kind: 'OBJECT', name: 'MaskedFloat' } })).toBe(true)
      expect(isMaskedFieldType({ name: 'verified', type: { kind: 'OBJECT', name: 'MaskedBoolean' } })).toBe(true)
      expect(isMaskedFieldType({ name: 'signedAt', type: { kind: 'OBJECT', name: 'MaskedDateTimeOffset' } })).toBe(true)
      expect(isMaskedFieldType({ name: 'orderId', type: { kind: 'SCALAR', name: 'String' } })).toBe(false)
      expect(isMaskedFieldType({ name: 'orderId', type: null })).toBe(false)
    })

    it('maps introspected fields to selectors, carrying the masked flag through', () => {
      const selectors = toSubscriptionFieldSelectors([
        { name: 'orderId', type: { kind: 'SCALAR', name: 'String' } },
        { name: 'legalName', type: { kind: 'OBJECT', name: 'MaskedString' } },
      ])
      expect(selectors).toEqual([
        { name: 'orderId', masked: false },
        { name: 'legalName', masked: true },
      ])
    })

    it('expands a masked selector to its own { value masked erased } sub-selection, leaving an ordinary scalar field as a bare name', () => {
      const query = buildSubscriptionQuery('kyc', 'IdentityClaimSubmitted', [
        { name: 'applicantId', masked: false },
        { name: 'claimedLegalName', masked: true },
      ])
      expect(query).toBe('subscription { on_kyc_identityclaimsubmitted(mode: TAIL) { applicantId claimedLegalName { value masked erased } } }')
    })

    it('still accepts a plain string array unchanged, the same shape every pre-existing caller already passes', () => {
      const query = buildSubscriptionQuery('mvvm-demo-1', 'OrderPlaced', ['orderId', 'amount'])
      expect(query).toBe('subscription { on_mvvm_demo_1_orderplaced(mode: TAIL) { orderId amount } }')
    })
  })
})
