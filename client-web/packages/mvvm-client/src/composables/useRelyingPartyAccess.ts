import { fetchToken, exchangeUcanDelegationForToken } from '../api/authClient'
import { registerTrustRoot } from '../api/rbacClient'
import { generateUcanKeyPair, computeJwkThumbprint, signUcanDelegation, type DelegatedCapability } from '../api/ucan'
import { graphqlQuery } from '../api/graphqlClient'
import type { FetchTokenFn } from './useEntityViewActions'

// ADR-043/044 "Delegated Grants, RBAC, Federated Claims" -- Meridian's
// Workflow B (Relying-Party Access) end to end, client-side: a customer
// self-issues a UCAN delegation (no pre-existing account/credential of
// their own needed -- their freshly-generated DID key IS the root of
// trust, ADR-044), a relying party exchanges it for a scoped access
// token, then reveals exactly the one masked field the delegation names,
// for exactly the one entity it names. Mirrors
// MeridianWorkflowBHttpSqliteTests.cs's own proven server-side sequence
// (ACustomerDelegatesACappedEntityScopedTimeBoxedGrantAndTheRelyingPartyRevealsTheUnlockedFieldForThatApplicantOnly),
// the first client-web UI surface for it (TODO.md).
export interface RelyingPartyAccessConfig {
  hostBaseUrl: string
  authBaseUrl: string
  appId: string
  // registry:trust-admin is needed to register the customer's own DID key
  // as an AppTrustRoot -- a real deployment would have its own onboarding
  // flow do this automatically/administratively, never exposed to an end
  // user directly; this dev/POC IdP's own already-public, dev-only
  // "operator-client" credential (EventStore.DevIdp/DevIdpSeeder.cs)
  // stands in for that step here, the same posture composer-client/
  // vitals-pi-client/meridian-analyst-client already establish for other
  // panels in this same app.
  trustAdminClientId?: string
  trustAdminClientSecret?: string
}

export interface RelyingPartyGrantRequest {
  granterActorId: string
  granteeActorId: string
  granteeClientId: string
  granteeClientSecret: string
  capability: DelegatedCapability
  entityId: string
  eventId: string
  fieldPath: string
  validForSeconds?: number
}

export interface RelyingPartyAccessResult {
  ok: boolean
  value?: string | null
  issuerDid?: string
  error?: string
}

export function useRelyingPartyAccess(config: RelyingPartyAccessConfig, deps: { fetchToken?: FetchTokenFn; sleep?: (ms: number) => Promise<void> } = {}) {
  const tokenFetcher = deps.fetchToken ?? fetchToken
  const sleep = deps.sleep ?? ((ms: number) => new Promise((resolve) => setTimeout(resolve, ms)))
  const trustAdminClientId = config.trustAdminClientId ?? 'operator-client'
  const trustAdminClientSecret = config.trustAdminClientSecret ?? 'operator-client-secret'

  async function grantAndReveal(request: RelyingPartyGrantRequest): Promise<RelyingPartyAccessResult> {
    const customerKeyPair = await generateUcanKeyPair()
    const issuerDid = await computeJwkThumbprint(customerKeyPair.publicJwk)

    try {
      const trustAdminToken = await tokenFetcher(config.authBaseUrl, trustAdminClientId, trustAdminClientSecret, 'registry:trust-admin')
      await registerTrustRoot(config.hostBaseUrl, trustAdminToken, config.appId, issuerDid, `Self-issued by ${request.granterActorId} via client-web's Relying-Party Access panel`)
    } catch (error) {
      return { ok: false, issuerDid, error: `Trust root registration failed: ${(error as Error).message}` }
    }

    const delegation = await signUcanDelegation(
      customerKeyPair,
      request.granterActorId,
      request.granteeActorId,
      config.appId,
      [request.capability],
      request.validForSeconds ?? 24 * 60 * 60,
    )

    // EventStore.DevIdp's own RbacProjectionWorker follows AppTrustRootRegistered
    // asynchronously (a real Follow tail, not a synchronous write) -- the
    // trust root registered just above isn't necessarily visible to the
    // token endpoint's own validation the instant the PUT above returns.
    // Retried, not a fixed sleep, matching every other REPLAY-mode
    // catch-up wait already used elsewhere in this codebase.
    let grantedToken: string | null = null
    let lastError: Error | null = null
    for (let attempt = 0; attempt < 10 && grantedToken === null; attempt++) {
      if (attempt > 0) await sleep(500)
      try {
        grantedToken = await exchangeUcanDelegationForToken(config.authBaseUrl, config.appId, request.granteeClientId, request.granteeClientSecret, delegation)
      } catch (error) {
        lastError = error as Error
      }
    }
    if (grantedToken === null) return { ok: false, issuerDid, error: `Token exchange failed after retrying: ${lastError?.message}` }

    try {
      const result = await graphqlQuery<{ revealField: { value: string | null } }>(
        config.hostBaseUrl,
        grantedToken,
        // eventId is a C# Guid server-side (RevealFieldMutation.cs) --
        // HotChocolate's own default scalar binding for Guid is UUID, not
        // the generic ID scalar; found only by actually calling this
        // mutation for real (a "variable is not compatible with the type
        // of the current location" GraphQL error), no prior client-web
        // caller of revealField existed to have already gotten this right.
        `mutation($entityId: String!, $eventId: UUID!, $fieldPath: String!) { revealField(entityId: $entityId, eventId: $eventId, fieldPath: $fieldPath) { value } }`,
        { entityId: request.entityId, eventId: request.eventId, fieldPath: request.fieldPath },
      )
      return { ok: true, value: result.revealField.value, issuerDid }
    } catch (error) {
      return { ok: false, issuerDid, error: `Reveal failed: ${(error as Error).message}` }
    }
  }

  return { grantAndReveal }
}
