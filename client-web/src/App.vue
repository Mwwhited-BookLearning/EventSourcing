<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useEntityViewActions, type ClientConfig } from './composables/useEntityViewActions'
import { useOnlineStatus } from './composables/useOnlineStatus'
import { useOutboxStore } from './stores/outbox'
import { useEntityCacheStore } from './stores/entityCache'
import { useViewDefinitionsStore } from './stores/viewDefinitions'
import EntityView from './components/entity/EntityView.vue'
import EntityBrowser from './components/entity/EntityBrowser.vue'
import EventComposer from './components/composer/EventComposer.vue'
import VitalsPiQueue from './components/queue/VitalsPiQueue.vue'
import MeridianAnalystQueue from './components/queue/MeridianAnalystQueue.vue'
import { tokens } from './theme/tokens'

// Per-instance launch configuration (ADR-039: "which EntityType/AppId/
// subscription target an instance follows is per-instance launch
// configuration, not a global singleton") -- read from the URL's own query
// string, so two windows of this same installed app can genuinely watch
// different things, exactly as the ADR describes. Defaults match this
// repo's own demo AppId/EventType conventions used throughout the server-
// side test suite.
const params = new URLSearchParams(window.location.search)
const config: ClientConfig = {
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

const outbox = useOutboxStore()
const entityCache = useEntityCacheStore()
const viewDefinitions = useViewDefinitionsStore()
const viewActions = useEntityViewActions(config)

const currentEntityId = ref('')
const amountInput = ref('')
const statusMessage = ref('')
// "Proving-Ground Application UX" -- a plain tab switcher, not a router
// dependency: this instance still watches exactly one EntityType/AppId
// (ADR-039's own per-instance launch configuration, unchanged), these are
// different VIEWS over that same subscription/outbox state, not
// different routes/pages.
const activeTab = ref<'detail' | 'browser' | 'composer' | 'queue'>('detail')

// "Domain Decision Queues" -- the one place in this otherwise domain-
// agnostic shell that knows Vitals is "trial1" and Meridian is "kyc"
// (EventStore.AppHost/AppHost.cs's own VITE_APP_ID values) -- a bespoke
// per-domain screen is, by definition, not generic, so this tab simply
// doesn't render for any OTHER AppId (a standalone `npm run dev` against
// the "mvvm-demo" fallback config, or a future third domain) rather than
// guessing at a queue that doesn't apply.
const queueDomain = computed<'vitals' | 'meridian' | null>(() => {
  if (config.appId === 'trial1') return 'vitals'
  if (config.appId === 'kyc') return 'meridian'
  return null
})

function selectFromBrowser(entityId: string): void {
  currentEntityId.value = entityId
  activeTab.value = 'detail'
}

const { isOnline } = useOnlineStatus(() => {
  void viewActions.flush().then(() => (statusMessage.value = 'Reconnected -- outbox flushed.'))
})

for (const [name, value] of Object.entries(tokens)) document.documentElement.style.setProperty(name, value)

onMounted(async () => {
  await outbox.loadFromDb(config.instanceId)
  await entityCache.loadFromDb(config.instanceId)
  await viewDefinitions.loadFromDb()
  await viewActions.subscribe((entityId) => {
    currentEntityId.value = entityId
  })
})

onUnmounted(() => viewActions.stopSubscription())

async function submitAmountCommand(): Promise<void> {
  if (!currentEntityId.value) return
  await viewActions.dispatchCommand(currentEntityId.value, { Amount: Number(amountInput.value) })
  statusMessage.value = isOnline.value ? 'Command dispatched.' : 'Offline -- queued in the local outbox.'
}

const pendingCount = computed(() => outbox.pendingFor(config.instanceId).length)
</script>

<template>
  <main>
    <header>
      <h1>Duplex Client</h1>
      <p>
        instance <code>{{ config.instanceId }}</code> — watching
        <code>{{ config.appId }}:{{ config.entityType }}</code> —
        <strong>{{ isOnline ? 'online' : 'offline' }}</strong>
        — {{ pendingCount }} command(s) queued
      </p>
    </header>

    <nav aria-label="View">
      <button type="button" :aria-pressed="activeTab === 'detail'" @click="activeTab = 'detail'">Detail</button>
      <button type="button" :aria-pressed="activeTab === 'browser'" @click="activeTab = 'browser'">Browse</button>
      <button type="button" :aria-pressed="activeTab === 'composer'" @click="activeTab = 'composer'">Compose</button>
      <button v-if="queueDomain" type="button" :aria-pressed="activeTab === 'queue'" @click="activeTab = 'queue'">Queue</button>
    </nav>

    <template v-if="activeTab === 'detail'">
      <section v-if="currentEntityId">
        <EntityView :entity-id="currentEntityId" :instance-id="config.instanceId" :entity-type="config.entityType" :view-actions="viewActions" />
      </section>
      <section v-else>
        <p>Waiting for the first event on this subscription…</p>
      </section>

      <section>
        <h2>Dispatch a command</h2>
        <label>
          Amount
          <input v-model="amountInput" type="number" />
        </label>
        <button type="button" :disabled="!currentEntityId" @click="submitAmountCommand">Set Amount</button>
        <p>{{ statusMessage }}</p>
      </section>
    </template>

    <EntityBrowser v-else-if="activeTab === 'browser'" :instance-id="config.instanceId" @select="selectFromBrowser" />

    <EventComposer v-else-if="activeTab === 'composer'" :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />

    <template v-else-if="activeTab === 'queue' && queueDomain">
      <VitalsPiQueue v-if="queueDomain === 'vitals'" :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />
      <MeridianAnalystQueue v-else :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />
    </template>
  </main>
</template>

<style>
body {
  background: var(--duplex-bg);
  color: var(--duplex-fg);
  font-family: system-ui, sans-serif;
}
</style>
