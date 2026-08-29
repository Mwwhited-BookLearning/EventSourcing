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

  it('derives form fields from a JSON schema, marking masked/object fields non-editable, with no RequiredSignature', async () => {
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
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ data: { eventType: { jsonSchema: JSON.stringify(schema), requiredSignature: null } } }))
    const detail = await composer.getEventTypeDetail('PatientScreened', 1)
    expect(detail.fields).toEqual([
      { name: 'SubjectId', type: 'string', required: true, editable: true },
      { name: 'LegalName', type: 'string', required: false, editable: false },
      { name: 'Nested', type: 'object', required: false, editable: false },
    ])
    expect(detail.requiredSignature).toBeNull()
  })

  it('surfaces RequiredSignature (ADR-066) off the same eventType query', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    const schema = { type: 'object', properties: { Finding: { type: 'string' } }, required: ['Finding'] }
    global.fetch = vi.fn().mockResolvedValue(
      jsonResponse({ data: { eventType: { jsonSchema: JSON.stringify(schema), requiredSignature: { acrValues: ['urn:test:step-up'], maxAge: 300 } } } }),
    )
    const detail = await composer.getEventTypeDetail('AuthorityDecisionRecorded', 1)
    expect(detail.requiredSignature).toEqual({ acrValues: ['urn:test:step-up'], maxAge: 300 })
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

  it('sends the Meaning envelope field when provided', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ status: 'received', entityId: 'trial1:Decision:D-1', conflictFlag: false }))
    await composer.publish('AuthorityDecisionRecorded', { Finding: 'x' }, 'reviewed')
    const [, init] = (global.fetch as ReturnType<typeof vi.fn>).mock.calls[0]
    expect(JSON.parse(init.body).meaning).toBe('reviewed')
  })

  it('reports a failed publish rather than throwing', async () => {
    const composer = useEventComposer({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' }, { fetchToken })
    global.fetch = vi.fn().mockResolvedValue(new Response('', { status: 403 }))
    const result = await composer.publish('PatientScreened', { SubjectId: 'S-0099' })
    expect(result.ok).toBe(false)
  })

  // ADR-066/RFC 9470 -- the actual step-up-then-retry flow: a 401 challenge
  // on the first attempt (no acr on the composer's ordinary token) is
  // resolved by fetching a NEW token with the challenge's own acrValues,
  // then retrying the SAME publish once, transparently to the caller.
  it('steps up authentication and retries once when the server issues an RFC 9470 challenge', async () => {
    const stepUpFetchToken = vi
      .fn()
      .mockResolvedValueOnce('composer-token')
      .mockResolvedValueOnce('composer-token-stepped-up')
    const composer = useEventComposer(
      { hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'trial1' },
      { fetchToken: stepUpFetchToken },
    )
    // Mirrors PublishEndpoints.cs's own BuildStepUpChallenge exactly: an
    // RFC 7807 ProblemDetails body (type/title/status), "title" carrying
    // "insufficient_user_authentication" -- there is no "error" field in
    // the real response body at all. This mock originally used {error:
    // ...} instead, matching a bug in publishClient.ts's own check
    // rather than the real server response -- found only by actually
    // driving a real RequiredSignature-gated publish through a live
    // server, where the mismatch meant this retry path never actually
    // fired. Locking the real wire shape here now, not the bug's own
    // assumption.
    const challengeResponse = new Response(
      JSON.stringify({
        type: 'https://eventstore.example/problems/insufficient-user-authentication',
        title: 'insufficient_user_authentication',
        status: 401,
        acrValues: ['urn:test:step-up'],
        maxAge: 300,
      }),
      { status: 401, headers: { 'content-type': 'application/json' } },
    )
    const acceptedResponse = jsonResponse({ status: 'received', entityId: 'trial1:Decision:D-1', conflictFlag: false })
    global.fetch = vi.fn().mockResolvedValueOnce(challengeResponse).mockResolvedValueOnce(acceptedResponse)

    const result = await composer.publish('AuthorityDecisionRecorded', { Finding: 'x' }, 'reviewed')

    expect(result).toEqual({ ok: true, status: 'received', entityId: 'trial1:Decision:D-1', conflictFlag: false, steppedUp: true })
    expect(stepUpFetchToken).toHaveBeenCalledWith('https://auth', 'composer-client', 'composer-client-secret', 'events:publish registry:admin', 'urn:test:step-up')
    const secondCallToken = (global.fetch as ReturnType<typeof vi.fn>).mock.calls[1][1].headers.Authorization
    expect(secondCallToken).toBe('Bearer composer-token-stepped-up')
  })
})
