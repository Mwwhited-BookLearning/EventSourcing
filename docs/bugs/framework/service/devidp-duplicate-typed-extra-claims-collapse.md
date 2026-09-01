[← Bugs index](../../../changes/2026-08-31.md)

# DevIdp silently drops all but the last of a client's same-typed extra claims

**Scope**: `framework` · **Tier**: `service`

## What was wrong

`vitals-pi-client`'s issued access token never actually carried the
`review:ae` claim, only `review:ionm` — even though
`DevIdpSeeder.ExtraClaims` lists both:
`["vitals-pi-client"] = [("review", "ae"), ("review", "ionm"), ("consent", "approve")]`.
Decoding a real, just-issued token's own JWT payload showed:
`{"review":"ionm","consent":"approve",...}` — no `"ae"` value anywhere.

## How and where it was found

Found while live-verifying the new flow engine's `myTasks` GraphQL query
(ADR-101) through a real browser: a real `AdverseEventReported` was
published, a `PendingTask` row was confirmed present in the database
directly, but `MyTasksView.vue` — authenticating as `vitals-pi-client`,
the same identity `VitalsPiQueue.vue` already uses for the Workflow B
"review:ae" decision — rendered "Nothing pending right now." A throwaway
Playwright script (`scratchpad/PlaywrightCheck`) with a `page.Request`
listener that intercepts every `/graphql` call and base64-decodes its
`Authorization: Bearer` JWT payload directly (no library, just the raw
three-part split + `Convert.FromBase64String`) showed the token itself
never carried `review:ae` at all — isolating the bug to token issuance,
not to `PendingTaskQueries`' own claim-filtering logic (which was
separately confirmed correct via `scratchpad/QueryCheck`, calling
`PendingTaskQueries.GetMyTasksAsync` directly with a hand-built
`ClaimsPrincipal` carrying `review:ae`, which correctly returned the
task).

No existing test caught this because no existing Playwright playbook
ever exercises `vitals-pi-client`'s `review:ae` claim through a real
DevIdp-issued token — `VitalsPrincipalInvestigatorQueuePlaybookTests.cs`
only ever decides Workflow D's `review:ionm` alerts, and every Workflow B
verification elsewhere in this repo used an in-process `ClaimsPrincipal`
test double, which never goes through DevIdp's own token-issuance code
at all.

## Root cause

`src/EventStore.DevIdp/Program.cs`'s token-issuance code looped over
`DevIdpSeeder.GetExtraClaims(request.ClientId!)` and called
`identity.SetClaim(claimType, claimValue)` per tuple.
`ClaimsIdentity.SetClaim` (the OpenIddict extension method) **replaces**
any existing claim of that same type — it is a single-valued setter, not
an additive one. Looping `[("review","ae"), ("review","ionm"), ("consent","approve")]`
therefore left only the *last* `"review"`-typed tuple's value
(`"ionm"`) in the identity; `"ae"` was set, then immediately overwritten.
The same file already uses the correct `identity.AddClaim(new Claim(...))`
pattern for two other multi-valued claim sources (RBAC's flattened
permissions, delegated-grant capabilities/federated claims) — this was
the one remaining call site still using the wrong method for a claim set
that can legitimately hold more than one value of the same type.

## Resolution

`src/EventStore.DevIdp/Program.cs`: changed the extra-claims loop from
`identity.SetClaim(claimType, claimValue)` to
`identity.AddClaim(new Claim(claimType, claimValue))`, matching the
sibling call sites already in this file. `identity.SetDestinations(_ =>
[Destinations.AccessToken])` (unchanged, a few lines below) applies
uniformly to claims added either way, so no destination-routing change
was needed.

**Regression test**:
`EventStore.IntegrationTests.AuthScenarioAssertions.AClientSeededWithMultipleSameTypedExtraClaimsGetsAllOfThemInTheIssuedToken`,
called from `AuthSqliteTests.AllAuthScenarios` — fetches a real
`vitals-pi-client` token from a real `WebApplicationFactory`-hosted
DevIdp, decodes it with `JwtSecurityTokenHandler`, and asserts both
`review:ae` and `review:ionm` (plus the unrelated `consent:approve`)
are all present. Confirmed **red** by temporarily reverting the fix
back to `SetClaim` locally and re-running
`AuthSqliteTests.AllAuthScenarios` (failed, as expected), then confirmed
**green** again with the fix restored. Full `AuthSqliteTests` (4/4) and
`EventStore.UnitTests` (86/86) pass.

No ADR update: an internal token-issuance bug in a dev/POC IdP's own
seeding code, not a change to any decided claim shape or contract
(`ADR-043`'s own `vitals-pi-client`/`meridian-analyst-client` claim
union design is unchanged — this fixes an implementation bug that
violated that design, it doesn't revise it).
