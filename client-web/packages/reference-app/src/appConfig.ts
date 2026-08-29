import type { ClientConfig } from '@eventstore/mvvm-client'

// Per-instance launch configuration (ADR-039: "which EntityType/AppId/
// subscription target an instance follows is per-instance launch
// configuration, not a global singleton") -- read from the URL's own query
// string, so two windows of this same installed app can genuinely watch
// different things, exactly as the ADR describes. Defaults match this
// repo's own demo AppId/EventType conventions used throughout the server-
// side test suite. Extracted out of App.vue (ADR-099) so the Vue Router
// navigation guard below can read `queueDomain` without depending on
// component setup timing -- this only ever reads the URL/env, never Pinia,
// so module-scope evaluation is safe and behaviorally identical to the
// prior read-once-in-setup() version.
const params = new URLSearchParams(window.location.search)
export const config: ClientConfig = {
  instanceId: params.get('instanceId') ?? crypto.randomUUID(),
  // Query string wins if present (a specific launch always overrides);
  // otherwise the Vite build-time env vars EventStore.AppHost injects via
  // WithEnvironment (a per-domain client-web resource -- clientWebVitals/
  // clientWebMeridian -- pre-configured for its own AppId/EntityType/
  // EventType, the same reasoning hostBaseUrl/authBaseUrl below already
  // established for the dynamically-assigned Aspire endpoint) win over the
  // hardcoded fallback, which only applies when running client-web
  // standalone (`npm run dev`, no AppHost at all).
  appId: params.get('appId') ?? import.meta.env.VITE_APP_ID ?? 'mvvm-demo',
  entityType: params.get('entityType') ?? import.meta.env.VITE_ENTITY_TYPE ?? 'orderplaced',
  eventType: params.get('eventType') ?? import.meta.env.VITE_EVENT_TYPE ?? 'OrderPlaced',
  entityIdField: params.get('entityIdField') ?? import.meta.env.VITE_ENTITY_ID_FIELD ?? 'orderId',
  hostBaseUrl: params.get('hostBaseUrl') ?? import.meta.env.VITE_HOST_BASE_URL ?? 'https://localhost:5001',
  // http, not https -- devIdp's own OpenIddict issuer is computed per-
  // request from whichever endpoint actually receives the call, and
  // eventstore's own Authentication:Authority trusts devIdp's HTTP
  // endpoint specifically (avoids an HTTPS metadata fetch at server
  // startup, EventStore.AppHost/AppHost.cs's own comment on that config
  // value). A token fetched via devIdp's https endpoint carries an
  // issuer eventstore doesn't trust, and every later GraphQL call fails
  // with "Forbidden -- caller's token does not hold the required scope"
  // -- found only by actually driving a real token through both
  // endpoints and comparing, not from reading the config alone.
  authBaseUrl: params.get('authBaseUrl') ?? import.meta.env.VITE_AUTH_BASE_URL ?? 'http://localhost:5010',
  clientId: params.get('clientId') ?? 'follower-client',
  clientSecret: params.get('clientSecret') ?? 'follower-client-secret',
  scope: params.get('scope') ?? 'events:follow',
}

// "Domain Decision Queues" -- the one place in this otherwise domain-
// agnostic shell that knows Vitals is "trial1" and Meridian is "kyc"
// (EventStore.AppHost/AppHost.cs's own VITE_APP_ID values) -- a bespoke
// per-domain screen is, by definition, not generic, so the Queue/Relying-
// Party routes simply don't exist for any OTHER AppId (a standalone
// `npm run dev` against the "mvvm-demo" fallback config, or a future third
// domain) rather than guessing at a queue that doesn't apply. A plain
// constant, not a Vue `computed`, since `config.appId` never changes after
// initial load -- both `router.ts`'s navigation guard and App.vue's own
// menu-filtering read this same value.
export type QueueDomain = 'vitals' | 'meridian' | null

export const queueDomain: QueueDomain = config.appId === 'trial1' ? 'vitals' : config.appId === 'kyc' ? 'meridian' : null
