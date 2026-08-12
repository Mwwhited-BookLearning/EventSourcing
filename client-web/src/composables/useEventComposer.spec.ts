import { describe, expect, it, vi } from 'vitest'
import { useEventComposer } from './useEventComposer'

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } })
}

describe('useEventComposer', () => {
  const fetchToken = vi.fn().mockResolvedValue('composer-token')

  it('fetches a token as a second, composer-scoped identity, distinct from the instance config', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ data: { eventTypes: [] } }))
    await composer.listEventTypes()
    expect(fetchToken).toHaveBeenCalledWith('https://auth', 'composer-client', 'composer-client-secret', 'events:publish registry:admin')
  })

  it('lists registered event types via the eventTypes GraphQL query', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ data: { eventTypes: [{ name: 'PatientScreened', version: 1, entityType: 'Patient', isActive: true }] } }),
    )
    const types = await composer.listEventTypes()
    expect(types).toEqual([{ name: 'PatientScreened', version: 1, entityType: 'Patient', isActive: true }])
  })

  // Found live: repeated re-registration across dev-iteration AppHost
  // restarts (no true registration idempotency, a pre-existing, separately
  // tracked characteristic) leaves several stale, inactive versions behind
  // per event type -- the dropdown must not flood with them.
  it('filters out inactive (superseded) schema versions', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({
        data: {
          eventTypes: [
            { name: 'PatientScreened', version: 1, entityType: 'Patient', isActive: false },
            { name: 'PatientScreened', version: 2, entityType: 'Patient', isActive: true },
          ],
        },
      }),
    )
    const types = await composer.listEventTypes()
    expect(types).toEqual([{ name: 'PatientScreened', version: 2, entityType: 'Patient', isActive: true }])
  })

  it('derives form fields from a JSON schema, marking masked/object fields non-editable', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    const schema = {
      type: 'object',
      properties: {
        SubjectId: { type: 'string' },
        LegalName: { type: 'string', 'x-masking': { strategy: 'FixedValue', requiredClaim: 'clearance:phi', maskedValue: 'REDACTED' } },
        Nested: { type: 'object' },
      },
      required: ['SubjectId'],
    }
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ data: { eventType: { jsonSchema: JSON.stringify(schema) } } }))
    const fields = await composer.getFormFields('PatientScreened', 1)
    expect(fields).toEqual([
      { name: 'SubjectId', type: 'string', required: true, editable: true },
      { name: 'LegalName', type: 'string', required: false, editable: false },
      { name: 'Nested', type: 'object', required: false, editable: false },
    ])
  })

  it('publishes via the ordinary POST /publish/{eventType} path, not the shared outbox', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ status: 'received', entityId: 'trial1:Patient:S-0099', conflictFlag: false }))
    const result = await composer.publish('PatientScreened', { SubjectId: 'S-0099' })
    expect(result).toEqual({ ok: true, status: 'received', entityId: 'trial1:Patient:S-0099', conflictFlag: false })
    const [url, init] = (global.fetch as ReturnType<typeof vi.fn>).mock.calls[0]
    expect(url).toBe('https://host/publish/PatientScreened')
    expect(JSON.parse(init.body).appId).toBe('trial1')
    expect(JSON.parse(init.body).payload).toBe(JSON.stringify({ SubjectId: 'S-0099' }))
  })

  it('reports a failed publish rather than throwing', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(new Response('', { status: 403 }))
    const result = await composer.publish('PatientScreened', { SubjectId: 'S-0099' })
    expect(result.ok).toBe(false)
  })
})
