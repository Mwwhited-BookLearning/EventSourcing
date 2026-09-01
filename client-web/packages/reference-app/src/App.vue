<script setup lang="ts">
import { computed, h, onMounted, onUnmounted, provide, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { NConfigProvider, NLayout, NLayoutContent, NLayoutHeader, NLayoutSider, NMenu, type MenuOption } from 'naive-ui'
import { useEntityViewActions, useOnlineStatus, useOutboxStore, useEntityCacheStore, useViewDefinitionsStore, tokens, themeOverrides } from '@eventstore/mvvm-client'
import { config, queueDomain } from './appConfig'
import { APP_STATE_KEY } from './appState'

const route = useRoute()
const router = useRouter()

const outbox = useOutboxStore()
const entityCache = useEntityCacheStore()
const viewDefinitions = useViewDefinitionsStore()
const viewActions = useEntityViewActions(config)

const currentEntityId = ref('')
const amountInput = ref('')
const statusMessage = ref('')

function selectFromBrowser(entityId: string): void {
  currentEntityId.value = entityId
  void router.push('/detail')
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

provide(APP_STATE_KEY, { config, viewActions, currentEntityId, amountInput, statusMessage, submitAmountCommand, selectFromBrowser })

// ADR-099 -- left-hand navigation rail (Azure Portal/DevOps-style),
// replacing the prior top tab-button row. Real `<a>` elements via
// RouterLink (not a plain @click handler), so deep-linking/"open in new
// tab"/screen-reader link semantics all work -- n-menu's own `value` just
// drives which item shows as active, navigation happens through the link.
function navLabel(to: string, text: string) {
  return () => h(RouterLink, { to }, { default: () => text })
}

const menuOptions = computed<MenuOption[]>(() => {
  const items: MenuOption[] = [
    { label: navLabel('/detail', 'Detail'), key: '/detail' },
    { label: navLabel('/browse', 'Browse'), key: '/browse' },
    { label: navLabel('/compose', 'Compose'), key: '/compose' },
    { label: navLabel('/tasks', 'My Tasks'), key: '/tasks' },
  ]
  if (queueDomain) items.push({ label: navLabel('/queue', 'Queue'), key: '/queue' })
  if (queueDomain === 'meridian') items.push({ label: navLabel('/relying-party', 'Relying-Party Access'), key: '/relying-party' })
  items.push({ label: navLabel('/lineage', 'Lineage & Playback'), key: '/lineage' })
  return items
})

// Sidebar collapse state is a per-viewer UI convenience (ADR-099), not app
// state -- localStorage, wrapped in try/catch (private browsing/blocked
// site data must never break the shell).
function loadCollapsedPreference(): boolean {
  try {
    return localStorage.getItem('duplex-nav-collapsed') === 'true'
  } catch {
    return false
  }
}

const collapsed = ref(loadCollapsedPreference())

function onSiderUpdateCollapsed(value: boolean): void {
  collapsed.value = value
  try {
    localStorage.setItem('duplex-nav-collapsed', String(value))
  } catch {
    // storage unavailable (private browsing, blocked site data) -- the
    // preference just doesn't persist across reloads, the shell itself
    // still works fine for this session.
  }
}
</script>

<template>
  <n-config-provider :theme-overrides="themeOverrides">
    <n-layout has-sider style="min-height: 100vh">
      <n-layout-sider bordered collapsible show-trigger :collapsed="collapsed" @update:collapsed="onSiderUpdateCollapsed">
        <div v-if="!collapsed" class="app-brand">
          <h1>Duplex Client</h1>
        </div>
        <n-menu :value="route.path" :collapsed="collapsed" :options="menuOptions" aria-label="View" />
      </n-layout-sider>
      <n-layout>
        <n-layout-header bordered class="app-header">
          <p>
            instance <code>{{ config.instanceId }}</code> — watching
            <code>{{ config.appId }}:{{ config.entityType }}</code> —
            <strong>{{ isOnline ? 'online' : 'offline' }}</strong>
            — {{ pendingCount }} command(s) queued
          </p>
        </n-layout-header>
        <n-layout-content class="app-content">
          <router-view />
        </n-layout-content>
      </n-layout>
    </n-layout>
  </n-config-provider>
</template>

<style>
body {
  background: var(--duplex-bg);
  color: var(--duplex-fg);
  font-family: system-ui, sans-serif;
  margin: 0;
}
.app-brand {
  padding: 1rem;
}
.app-header {
  padding: 0.75rem 1rem;
}
.app-content {
  padding: 1rem;
}
</style>
