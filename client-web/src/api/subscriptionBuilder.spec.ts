import { describe, expect, it } from 'vitest'
import { buildIntrospectionQuery, buildSubscriptionQuery, payloadTypeName, sanitizeGraphQlName, subscriptionFieldName } from './subscriptionBuilder'

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

  it('builds an introspection query naming the exact dynamically-built payload type', () => {
    expect(buildIntrospectionQuery('mvvm-demo-1', 'OrderPlaced')).toContain('__type(name: "mvvm_demo_1_orderplaced_Payload")')
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
})
