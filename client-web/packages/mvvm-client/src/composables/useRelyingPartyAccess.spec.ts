import { describe, expect, it, vi } from 'vitest'
import { useRelyingPartyAccess } from './useRelyingPartyAccess'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}

const request = {
  granterActorId: 'applicant-1001',
  granteeActorId: 'colleague-1',
  granteeClientId: 'colleague-client',
  granteeClientSecret: 'colleague-client-secret',
  capability: { claim: 'identity:pii-read', entityScope: 'kyc:applicantidentity:applicant-1001' },
  entityId: 'kyc:applicantidentity:applicant-1001',
  eventId: '00000000-0000-0000-0000-000000000001',
  fieldPath: '$.ClaimedLegalName',
}

describe('useRelyingPartyAccess (ADR-043/044 client-side, Meridian Workflow B)', () => {
  it('registers a trust root, exchanges the delegation, and reveals the field on the happy path', async () => {
    const fetchToken = vi.fn().mockResolvedValue('trust-admin-token')
    const sleep = vi.fn().mockResolvedValue(undefined)
    const fetchMock = vi
      .fn()
      // PUT /rbac/trust-roots/{issuerDid}
      .mockResolvedValueOnce(new Response('', { status: 201 }))
      // POST /connect/token (exchange)
      .mockResolvedValueOnce(jsonResponse({ access_token: 'granted-token' }))
      // revealField
      .mockResolvedValueOnce(jsonResponse({ data: { revealField: { value: 'Jane Smith' } } }))
    global.fetch = fetchMock

    const access = useRelyingPartyAccess({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'kyc' }, { fetchToken, sleep })
    const result = await access.grantAndReveal(request)

    expect(result.ok).toBe(true)
    expect(result.value).toBe('Jane Smith')
    expect(result.issuerDid).toBeTruthy()
    expect(sleep).not.toHaveBeenCalled()

    expect(fetchToken).toHaveBeenCalledWith('https://auth', 'operator-client', 'operator-client-secret', 'registry:trust-admin')

    const trustRootCall = fetchMock.mock.calls[0]
    expect(trustRootCall[0]).toBe(`https://host/rbac/trust-roots/${result.issuerDid}`)
    expect(trustRootCall[1].method).toBe('PUT')
    expect(JSON.parse(trustRootCall[1].body).appId).toBe('kyc')

    const exchangeCall = fetchMock.mock.calls[1]
    expect(exchangeCall[0]).toBe('https://auth/connect/token')
    const exchangeParams = new URLSearchParams(exchangeCall[1].body as string)
    expect(exchangeParams.get('grant_type')).toBe('urn:ietf:params:oauth:grant-type:token-exchange')
    expect(exchangeParams.get('client_id')).toBe('colleague-client')
    expect(exchangeParams.get('app_id')).toBe('kyc')

    const revealCall = fetchMock.mock.calls[2]
    expect(revealCall[1].headers.Authorization).toBe('Bearer granted-token')
  })

  // EventStore.DevIdp's own RbacProjectionWorker follows AppTrustRootRegistered
  // asynchronously -- the exchange can genuinely fail on the very first
  // attempt if it lands before that fold catches up. This is the real
  // reason grantAndReveal retries rather than calling exchange once.
  it('retries the token exchange until the trust root propagates, rather than failing on the first attempt', async () => {
    const fetchToken = vi.fn().mockResolvedValue('trust-admin-token')
    const sleep = vi.fn().mockResolvedValue(undefined)
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response('', { status: 201 })) // trust root registered
      .mockResolvedValueOnce(new Response('not yet a trust root', { status: 400 })) // exchange attempt 1: fails
      .mockResolvedValueOnce(jsonResponse({ access_token: 'granted-token' })) // exchange attempt 2: succeeds
      .mockResolvedValueOnce(jsonResponse({ data: { revealField: { value: 'Jane Smith' } } }))
    global.fetch = fetchMock

    const access = useRelyingPartyAccess({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'kyc' }, { fetchToken, sleep })
    const result = await access.grantAndReveal(request)

    expect(result.ok).toBe(true)
    expect(sleep).toHaveBeenCalledTimes(1)
  })

  it('reports failure with a real reason when the trust root registration itself is rejected (e.g. caller lacks registry:trust-admin)', async () => {
    const fetchToken = vi.fn().mockResolvedValue('insufficiently-scoped-token')
    global.fetch = vi.fn().mockResolvedValue(new Response('Forbidden', { status: 403 }))

    const access = useRelyingPartyAccess({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'kyc' }, { fetchToken })
    const result = await access.grantAndReveal(request)

    expect(result.ok).toBe(false)
    expect(result.error).toContain('Trust root registration failed')
  })

  it('reports failure when every exchange retry is exhausted', async () => {
    const fetchToken = vi.fn().mockResolvedValue('trust-admin-token')
    const sleep = vi.fn().mockResolvedValue(undefined)
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response('', { status: 201 })).mockResolvedValue(new Response('invalid_grant', { status: 400 }))
    global.fetch = fetchMock

    const access = useRelyingPartyAccess({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'kyc' }, { fetchToken, sleep })
    const result = await access.grantAndReveal(request)

    expect(result.ok).toBe(false)
    expect(result.error).toContain('Token exchange failed after retrying')
  })

  // ADR-043's own entity-scoping invariant: revealField itself rejects an
  // entityId the delegation's own capability never named -- this composable
  // must surface that as an ordinary failure, not throw uncaught.
  it('reports failure when revealField itself rejects the call (e.g. entity outside the delegation\'s own scope)', async () => {
    const fetchToken = vi.fn().mockResolvedValue('trust-admin-token')
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response('', { status: 201 }))
      .mockResolvedValueOnce(jsonResponse({ access_token: 'granted-token' }))
      .mockResolvedValueOnce(jsonResponse({ errors: [{ message: 'Forbidden -- caller lacks the required claim to reveal this field for this entity.' }] }))
    global.fetch = fetchMock

    const access = useRelyingPartyAccess({ hostBaseUrl: 'https://host', authBaseUrl: 'https://auth', appId: 'kyc' }, { fetchToken })
    const result = await access.grantAndReveal({ ...request, entityId: 'kyc:applicantidentity:applicant-1002' })

    expect(result.ok).toBe(false)
    expect(result.error).toContain('Reveal failed')
  })
})
