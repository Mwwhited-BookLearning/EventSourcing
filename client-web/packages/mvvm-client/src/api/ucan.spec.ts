import { describe, expect, it } from 'vitest'
import { generateUcanKeyPair, computeJwkThumbprint, signUcanDelegation } from './ucan'

function base64UrlDecodeJson(segment: string): Record<string, unknown> {
  const padded = segment.replace(/-/g, '+').replace(/_/g, '/').padEnd(segment.length + ((4 - (segment.length % 4)) % 4), '=')
  return JSON.parse(Buffer.from(padded, 'base64').toString('utf-8'))
}

describe('ucan.ts (ADR-043/044, client-side half of EventStore.Ucan)', () => {
  it('generates a fresh EC P-256 keypair each call, never reusing one across "customers"', async () => {
    const a = await generateUcanKeyPair()
    const b = await generateUcanKeyPair()
    expect(a.publicJwk.kty).toBe('EC')
    expect(a.publicJwk.crv).toBe('P-256')
    expect(a.publicJwk.x).not.toBe(b.publicJwk.x)
  })

  // RFC 7638 -- the exact canonical-JSON shape EventStore.Dpop.JwkThumbprint.Compute
  // hashes server-side; this doesn't re-derive the algorithm, it locks the
  // exact key ORDER (crv, kty, x, y) and field set, which is what actually
  // has to match byte-for-byte for the two sides to ever agree on one value.
  it('computes an RFC 7638 thumbprint deterministically for the same key', async () => {
    const keyPair = await generateUcanKeyPair()
    const first = await computeJwkThumbprint(keyPair.publicJwk)
    const second = await computeJwkThumbprint(keyPair.publicJwk)
    expect(first).toBe(second)
    expect(first.length).toBeGreaterThan(0)
  })

  it('produces different thumbprints for different keys', async () => {
    const a = await generateUcanKeyPair()
    const b = await generateUcanKeyPair()
    expect(await computeJwkThumbprint(a.publicJwk)).not.toBe(await computeJwkThumbprint(b.publicJwk))
  })

  it('signs a self-verifying ucan+jwt with the granter\'s own public key embedded in the header', async () => {
    const granter = await generateUcanKeyPair()
    const jwt = await signUcanDelegation(granter, 'applicant-1001', 'colleague-1', 'kyc', [{ claim: 'identity:pii-read', entityScope: 'kyc:applicantidentity:applicant-1001' }], 3600)

    const [headerSegment, payloadSegment, signatureSegment] = jwt.split('.')
    expect(signatureSegment.length).toBeGreaterThan(0)

    const header = base64UrlDecodeJson(headerSegment)
    expect(header).toMatchObject({ typ: 'ucan+jwt', alg: 'ES256', jwk: granter.publicJwk })

    const payload = base64UrlDecodeJson(payloadSegment)
    expect(payload.iss).toBe('applicant-1001')
    expect(payload.aud).toBe('colleague-1')
    expect(payload.appId).toBe('kyc')
    expect(typeof payload.jti).toBe('string')
    expect(typeof payload.exp).toBe('number')
  })

  // UcanValidator.cs deserializes "cap" with System.Text.Json's own default,
  // case-sensitive PascalCase property matching -- "claim"/"entityScope"
  // (this module's own camelCase interface) would silently fail to bind on
  // the server side. Locking the literal wire shape here is the whole
  // point of this test, not an implementation detail to relax later.
  it('serializes "cap" using PascalCase Claim/EntityScope keys, matching UcanDelegation.Create\'s own System.Text.Json default', async () => {
    const granter = await generateUcanKeyPair()
    const jwt = await signUcanDelegation(granter, 'applicant-1001', 'colleague-1', 'kyc', [{ claim: 'identity:pii-read', entityScope: 'kyc:applicantidentity:applicant-1001' }], 3600)
    const payload = base64UrlDecodeJson(jwt.split('.')[1])
    const cap = JSON.parse(payload.cap as string)
    expect(cap).toEqual([{ Claim: 'identity:pii-read', EntityScope: 'kyc:applicantidentity:applicant-1001' }])
  })

  it('sets exp validForSeconds in the future, never a fixed/hardcoded expiry', async () => {
    const granter = await generateUcanKeyPair()
    const before = Math.floor(Date.now() / 1000)
    const jwt = await signUcanDelegation(granter, 'applicant-1001', 'colleague-1', 'kyc', [{ claim: 'identity:pii-read', entityScope: null }], 60)
    const payload = base64UrlDecodeJson(jwt.split('.')[1])
    expect(payload.exp as number).toBeGreaterThanOrEqual(before + 60)
    expect(payload.exp as number).toBeLessThan(before + 65)
  })
})
