// RFC 9449 DPoP -- devIdp's /connect/token and every eventstore endpoint
// behind DpopValidationMiddleware (ADR-017) require a fresh, signed proof
// of possession on every request; this client had none at all until now.
// Found only by actually driving this client against a real DPoP-
// enforcing devIdp/eventstore in a real browser -- this repo's own
// server-side tests exercise DpopProofValidator directly, and no test
// anywhere drove this client's own fetch calls against it before.
//
// One ECDSA P-256 keypair, generated lazily and reused for this page
// load's lifetime -- the SAME key must sign both the /connect/token
// request (binding the issued access token's own cnf.jkt) and every
// later resource-server request presenting that token, or
// DpopValidationMiddleware's "DPoP proof key does not match the access
// token's cnf.jkt" check rejects every one of them.
let keyPairPromise: Promise<CryptoKeyPair> | null = null

function getKeyPair(): Promise<CryptoKeyPair> {
  keyPairPromise ??= crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']) as Promise<CryptoKeyPair>
  return keyPairPromise
}

function base64UrlEncode(bytes: ArrayBuffer): string {
  let binary = ''
  for (const byte of new Uint8Array(bytes)) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function base64UrlEncodeText(text: string): string {
  return base64UrlEncode(new TextEncoder().encode(text).buffer as ArrayBuffer)
}

// DpopProofValidator's own "ath" check (EventStore.Dpop, server-side) --
// the SHA-256 of the access token's own literal string bytes, required on
// every resource-server call once a token exists; never on the
// /connect/token call itself, which has no token yet to bind.
async function computeAth(accessToken: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(accessToken))
  return base64UrlEncode(digest)
}

// Mirrors EventStore.Dpop.DpopProofValidator's own required shape exactly:
// header { typ: "dpop+jwt", alg: "ES256", jwk: <EC public key> }, payload
// { htm, htu, iat, jti, ath? }, ES256-signed (WebCrypto's ECDSA signature
// for a named curve is already the raw r||s concatenation JOSE expects --
// no DER conversion needed). `htu` must match the server's own
// `scheme://host+pathBase+path` computation (query string excluded,
// DpopProofValidator's own callers already build it that way) -- callers
// here pass the exact request URL they're about to fetch, which is
// already in that shape for every call site in this client.
export async function createDpopProof(htm: string, htu: string, accessToken?: string): Promise<string> {
  const { publicKey, privateKey } = await getKeyPair()
  const jwk = await crypto.subtle.exportKey('jwk', publicKey)
  const header = { typ: 'dpop+jwt', alg: 'ES256', jwk: { kty: jwk.kty, crv: jwk.crv, x: jwk.x, y: jwk.y } }
  const payload: Record<string, unknown> = { htm, htu, iat: Math.floor(Date.now() / 1000), jti: crypto.randomUUID() }
  if (accessToken) payload.ath = await computeAth(accessToken)

  const signingInput = `${base64UrlEncodeText(JSON.stringify(header))}.${base64UrlEncodeText(JSON.stringify(payload))}`
  const signature = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, privateKey, new TextEncoder().encode(signingInput))
  return `${signingInput}.${base64UrlEncode(signature)}`
}
