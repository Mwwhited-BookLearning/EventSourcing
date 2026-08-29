// ADR-043/044 "Delegated Grants, RBAC, Federated Claims" -- Meridian's
// Workflow B (Relying-Party Access) proved this mechanism end to end
// server-side (MeridianWorkflowBHttpSqliteTests.cs) but had no client-web
// UI surface at all (TODO.md). This module is the client-side half of
// EventStore.Ucan/EventStore.Dpop: a customer's own ephemeral DID key
// (a fresh ECDSA P-256 WebCrypto keypair, never persisted -- the same
// "self-verifying JWT, signed by the granter's own key" shape
// UcanDelegation.cs documents, not full DID document resolution there
// either), used to sign a UCAN delegation JWT client-side. Deliberately
// mirrors dpop.ts's own header/signing shape exactly (same header
// {typ, alg: ES256, jwk}, same ES256-over-base64url-JSON construction) --
// the two are the same self-verifying-JWT primitive applied to different
// claim sets, exactly as DpopKeyPair.SignJwt's own server-side comment
// describes.
export interface DelegatedCapability {
  claim: string
  entityScope: string | null
}

function base64UrlEncode(bytes: ArrayBuffer): string {
  let binary = ''
  for (const byte of new Uint8Array(bytes)) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function base64UrlEncodeText(text: string): string {
  return base64UrlEncode(new TextEncoder().encode(text).buffer as ArrayBuffer)
}

export interface UcanKeyPair {
  keyPair: CryptoKeyPair
  publicJwk: { kty: string; crv: string; x: string; y: string }
}

export async function generateUcanKeyPair(): Promise<UcanKeyPair> {
  const keyPair = (await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify'])) as CryptoKeyPair
  const jwk = await crypto.subtle.exportKey('jwk', keyPair.publicKey)
  return { keyPair, publicJwk: { kty: jwk.kty!, crv: jwk.crv!, x: jwk.x!, y: jwk.y! } }
}

// RFC 7638 JWK thumbprint -- mirrors EventStore.Dpop.JwkThumbprint.Compute
// exactly: SHA-256 over the canonical JSON of exactly the EC "required
// members" (crv, kty, x, y), lexicographically ordered, no whitespace.
// This is the value registered as an AppTrustRoot's own IssuerDid, and
// what UcanValidator.cs recomputes from the delegation's own embedded jwk
// to confirm a match.
export async function computeJwkThumbprint(jwk: { kty: string; crv: string; x: string; y: string }): Promise<string> {
  const canonical = `{"crv":"${jwk.crv}","kty":"${jwk.kty}","x":"${jwk.x}","y":"${jwk.y}"}`
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonical))
  return base64UrlEncode(digest)
}

// Mirrors UcanDelegation.Create (EventStore.Ucan) exactly, including its
// own claim casing: "cap" is a JSON-serialized array of
// { "Claim": ..., "EntityScope": ... } objects (System.Text.Json's own
// default PascalCase property naming, no camelCase policy applied
// server-side -- UcanValidator.cs deserializes with that same default,
// case-sensitive) -- NOT the camelCase this module's own TypeScript
// interface uses for readability. No "prf" support (unlike the server
// type) -- this client only ever plays the "root of trust, no further
// proof needed" half of ADR-044, matching RegisterTrustRootAsync's own
// use in this same flow; a proof-chained sub-delegation is out of scope
// here the same way it's out of scope server-side (UcanDelegation.cs's
// own comment).
export async function signUcanDelegation(
  granterKeyPair: UcanKeyPair,
  issuerActorId: string,
  granteeActorId: string,
  appId: string,
  capabilities: DelegatedCapability[],
  validForSeconds: number,
): Promise<string> {
  const header = { typ: 'ucan+jwt', alg: 'ES256', jwk: granterKeyPair.publicJwk }
  const payload = {
    jti: crypto.randomUUID(),
    iss: issuerActorId,
    aud: granteeActorId,
    appId,
    cap: JSON.stringify(capabilities.map((c) => ({ Claim: c.claim, EntityScope: c.entityScope }))),
    exp: Math.floor(Date.now() / 1000) + validForSeconds,
  }
  const signingInput = `${base64UrlEncodeText(JSON.stringify(header))}.${base64UrlEncodeText(JSON.stringify(payload))}`
  const signature = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, granterKeyPair.keyPair.privateKey, new TextEncoder().encode(signingInput))
  return `${signingInput}.${base64UrlEncode(signature)}`
}
