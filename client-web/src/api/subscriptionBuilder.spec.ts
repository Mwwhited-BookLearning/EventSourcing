import { describe, expect, it } from 'vitest'
import { buildIntrospectionQuery, buildSubscriptionQuery, payloadTypeName, sanitizeGraphQlName, subscriptionFieldName } from './subscriptionBuilder'

describe('subscriptionBuilder (mirrors FollowSubscriptionTypeModule.Sanitize server-side)', () => {
  it('sanitizes non-alphanumeric characters the same way the server does', () => {
    expect(sanitizeGraphQlName('mvvm-demo-1')).toBe('mvvm_demo_1')
    expect(sanitizeGraphQlName('OrderPlaced')).toBe('OrderPlaced')
  })

  it('builds the payload type name in AppId_Name_Payload form', () => {
    expect(payloadTypeName('mvvm-demo-1', 'OrderPlaced')).toBe('mvvm_demo_1_OrderPlaced_Payload')
  })

  it('builds the subscription field name in on_AppId_Name form', () => {
    expect(subscriptionFieldName('mvvm-demo-1', 'OrderPlaced')).toBe('on_mvvm_demo_1_OrderPlaced')
  })

  it('builds an introspection query naming the exact dynamically-built payload type', () => {
    expect(buildIntrospectionQuery('mvvm-demo-1', 'OrderPlaced')).toContain('__type(name: "mvvm_demo_1_OrderPlaced_Payload")')
  })

  it('builds a subscription query requesting every introspected field', () => {
    const query = buildSubscriptionQuery('mvvm-demo-1', 'OrderPlaced', ['orderId', 'amount', 'conflictFlag'])
    expect(query).toBe('subscription { on_mvvm_demo_1_OrderPlaced(mode: TAIL) { orderId amount conflictFlag } }')
  })
})
