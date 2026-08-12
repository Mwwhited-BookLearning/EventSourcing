import { describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import EventComposer from './EventComposer.vue'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}

// crypto.subtle (DPoP proof generation, authClient.ts's own createDpopProof
// dependency) resolves via a macrotask, not a plain microtask -- same
// characteristic already documented/worked around in
// OfflineBundleViewer.spec.ts; flushPromises() alone isn't enough here either.
async function flushAll(): Promise<void> {
  await flushPromises()
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 5))
}

const props = { hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }

describe('EventComposer', () => {
  it('shows the Meaning field only for a RequiredSignature (ADR-066) event type, and gates Publish on it', async () => {
    global.fetch = vi
      .fn()
      // ensureComposerToken (listEventTypes)
      .mockResolvedValueOnce(jsonResponse({ access_token: 'composer-token' }))
      .mockResolvedValueOnce(jsonResponse({ data: { eventTypes: [{ name: 'AuthorityDecisionRecorded', version: 1, entityType: 'Alert', isActive: true }] } }))
      // getEventTypeDetail (cached token, no re-fetch)
      .mockResolvedValueOnce(
        jsonResponse({
          data: {
            eventType: {
              jsonSchema: JSON.stringify({ type: 'object', properties: { Finding: { type: 'string' } }, required: ['Finding'] }),
              requiredSignature: { acrValues: ['urn:test:step-up'], maxAge: 300 },
            },
          },
        }),
      )

    const wrapper = mount(EventComposer, { props })
    await flushAll()

    const select = wrapper.find('select')
    await select.setValue('AuthorityDecisionRecorded')
    await flushAll()

    expect(wrapper.find('[data-testid="composer-signature-block"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('urn:test:step-up')

    const publishButton = wrapper.find('button[type="submit"]')
    expect((publishButton.element as HTMLButtonElement).disabled).toBe(true)

    await wrapper.find('form').find('input').setValue('x') // Finding field (first input in the form)
    await wrapper.find('[data-testid="composer-meaning-input"]').setValue('reviewed')
    await flushAll()

    expect((publishButton.element as HTMLButtonElement).disabled).toBe(false)
  })

  it('shows no Meaning field for an ordinary event type with no RequiredSignature', async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ access_token: 'composer-token' }))
      .mockResolvedValueOnce(jsonResponse({ data: { eventTypes: [{ name: 'OrderPlaced', version: 1, entityType: 'Order', isActive: true }] } }))
      .mockResolvedValueOnce(
        jsonResponse({
          data: { eventType: { jsonSchema: JSON.stringify({ type: 'object', properties: { OrderId: { type: 'string' } }, required: ['OrderId'] }), requiredSignature: null } },
        }),
      )

    const wrapper = mount(EventComposer, { props })
    await flushAll()
    await wrapper.find('select').setValue('OrderPlaced')
    await flushAll()

    expect(wrapper.find('[data-testid="composer-signature-block"]').exists()).toBe(false)
  })
})
