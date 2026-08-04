// OAuth2 Client Credentials against EventStore.DevIdp -- mirrors this
// repo's own AuthScenarioAssertions.GetTokenAsync (C#) exactly, since this
// is the same dev/POC IdP every server-side test already talks to.
export async function fetchToken(
  authBaseUrl: string,
  clientId: string,
  clientSecret: string,
  scope: string,
): Promise<string> {
  const response = await fetch(`${authBaseUrl}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'client_credentials',
      client_id: clientId,
      client_secret: clientSecret,
      scope,
    }),
  })
  if (!response.ok) throw new Error(`Token request failed: ${response.status} ${await response.text()}`)
  const body = (await response.json()) as { access_token: string }
  return body.access_token
}
