import { createDpopProof } from './dpop'

// ADR-044 -- PUT /rbac/trust-roots/{issuerDid} (EventStore.Rbac/
// RbacEndpoints.cs), gated behind registry:trust-admin. Registers an
// AppTrustRoot: from this point on, a UcanDelegation self-signed by the
// matching private key is trusted as a genuine root of trust for this
// AppId, with no further proof chain needed (UcanValidator.cs's own
// "without a proof" branch).
export async function registerTrustRoot(hostBaseUrl: string, token: string, appId: string, issuerDid: string, description?: string): Promise<void> {
  const url = `${hostBaseUrl}/rbac/trust-roots/${issuerDid}`
  const response = await fetch(url, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      DPoP: await createDpopProof('PUT', url, token),
    },
    body: JSON.stringify({ appId, description }),
  })
  if (!response.ok) throw new Error(`Trust root registration failed: ${response.status} ${await response.text()}`)
}
