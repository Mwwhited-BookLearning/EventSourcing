<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useEntityViewActions, type ClientConfig } from './composables/useEntityViewActions'
import { useOnlineStatus } from './composables/useOnlineStatus'
import { useOutboxStore } from './stores/outbox'
import { useEntityCacheStore } from './stores/entityCache'
import { useViewDefinitionsStore } from './stores/viewDefinitions'
import EntityView from './components/entity/EntityView.vue'
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
  appId: params.get('appId') ?? 'mvvm-demo',
  entityType: params.get('entityType') ?? 'orderplaced',
  eventType: params.get('eventType') ?? 'OrderPlaced',
  entityIdField: params.get('entityIdField') ?? 'orderId',
  hostBaseUrl: params.get('hostBaseUrl') ?? 'https://localhost:5001',
  authBaseUrl: params.get('authBaseUrl') ?? 'https://localhost:5011',
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
  </main>
</template>

<style>
body {
  background: var(--duplex-bg);
  color: var(--duplex-fg);
  font-family: system-ui, sans-serif;
}
</style>
