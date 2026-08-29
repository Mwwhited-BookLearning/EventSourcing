import { createDpopProof } from './dpop'

// OAuth2 Client Credentials against EventStore.DevIdp -- mirrors this
// repo's own AuthScenarioAssertions.GetTokenAsync (C#) exactly, since this
// is the same dev/POC IdP every server-side test already talks to.
// `acr`, when passed, is ADR-066/RFC 9470's dev-only step-up simulation --
// EventStore.DevIdp/Program.cs's own "acr" form parameter, which stamps an
// `acr`/`auth_time` claim onto the issued token with no real re-
// authentication behind it (this IdP has no interactive login for a
// client_credentials caller to step up through at all). A real IdP would
// take the caller through an actual password/OTP/WebAuthn re-auth here
// instead; every existing caller that never passes `acr` is unaffected.
export async function fetchToken(
  authBaseUrl: string,
  clientId: string,
  clientSecret: string,
  scope: string,
  acr?: string,
): Promise<string> {
  const url = `${authBaseUrl}/connect/token`
  const params: Record<string, string> = {
    grant_type: 'client_credentials',
    client_id: clientId,
    client_secret: clientSecret,
    scope,
  }
  if (acr) params.acr = acr
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      DPoP: await createDpopProof('POST', url), // ADR-017 -- no access token exists yet to bind via "ath"
    },
    body: new URLSearchParams(params),
  })
  if (!response.ok) throw new Error(`Token request failed: ${response.status} ${await response.text()}`)
  const body = (await response.json()) as { access_token: string }
  return body.access_token
}

// RFC 8693 OAuth 2.0 Token Exchange, ADR-043/044's own mechanism for
// redeeming a UCAN delegation -- mirrors MeridianWorkflowBHttpSqliteTests.cs's
// own ExchangeAsync exactly. `subjectToken` is a self-signed UcanDelegation
// JWT (ucan.ts's own signUcanDelegation); "urn:eventstore:token-type:
// external-subject" is the fixed subject_token_type EventStore.DevIdp
// registers this grant type under -- not a real IANA-registered URN, this
// dev/POC IdP's own literal string. The resulting access token is bound
// (RFC 9449 cnf.jkt) to whichever key signs this call's own DPoP proof --
// createDpopProof's module-level singleton key here, the same key every
// other call from this page already proves possession of, so the granted
// token can be used immediately by any other client in this module
// without a second key to track.
export async function exchangeUcanDelegationForToken(authBaseUrl: string, appId: string, clientId: string, clientSecret: string, subjectToken: string): Promise<string> {
  const url = `${authBaseUrl}/connect/token`
  const params: Record<string, string> = {
    grant_type: 'urn:ietf:params:oauth:grant-type:token-exchange',
    subject_token: subjectToken,
    subject_token_type: 'urn:eventstore:token-type:external-subject',
    requested_token_type: 'urn:ietf:params:oauth:token-type:access_token',
    app_id: appId,
    client_id: clientId,
    client_secret: clientSecret,
  }
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
      DPoP: await createDpopProof('POST', url),
    },
    body: new URLSearchParams(params),
  })
  if (!response.ok) throw new Error(`Token exchange failed: ${response.status} ${await response.text()}`)
  const body = (await response.json()) as { access_token: string }
  return body.access_token
}
