<script setup lang="ts">
import { computed, h, onMounted, onUnmounted, provide, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { NButton, NConfigProvider, NLayout, NLayoutContent, NLayoutHeader, NLayoutSider, NMenu, type MenuOption } from 'naive-ui'
import { useEntityViewActions, useOnlineStatus, useOutboxStore, useEntityCacheStore, useViewDefinitionsStore, useConnectivityStore, resolveEventTypeFieldCasing, tokens, themeOverrides } from '@eventstore/mvvm-client'
import { config, queueDomain } from './appConfig'
import { APP_STATE_KEY } from './appState'

const route = useRoute()
const router = useRouter()

const outbox = useOutboxStore()
const entityCache = useEntityCacheStore()
const viewDefinitions = useViewDefinitionsStore()
const connectivity = useConnectivityStore()
const viewActions = useEntityViewActions(config)

const currentEntityId = ref('')
const amountInput = ref('')
const statusMessage = ref('')

function selectFromBrowser(entityId: string): void {
  currentEntityId.value = entityId
  void router.push('/detail')
}

// Real, automatic browser connectivity detection (navigator online/offline
// events, tracked reactively by useOnlineStatus's own isOnline ref) --
// unchanged, still the source of truth for genuine network state.
// `forceOfflineForDemo`/`forceOnlineForDemo` below layer a manual override
// on top via useConnectivityStore's `forcedOffline` state (plain reactive
// Pinia state, safe to read directly here -- unlike the store's own
// `isEffectivelyOnline()` method, which deliberately re-reads
// `navigator.onLine` fresh on every call rather than caching it, so it's
// called imperatively at each dispatch/capture point instead of woven into
// a template-reactive computed). The two combine into one effective value
// this header displays and the manual buttons toggle.
const { isOnline } = useOnlineStatus(() => {
  if (!connectivity.forcedOffline) void viewActions.flush().then(() => (statusMessage.value = 'Reconnected -- outbox flushed.'))
})

const effectivelyOnline = computed(() => isOnline.value && !connectivity.forcedOffline)

function forceOfflineForDemo(): void {
  connectivity.goOffline()
  statusMessage.value = 'Forced offline -- commands will queue in the local outbox.'
}

function forceOnlineForDemo(): void {
  connectivity.goOnline()
  if (isOnline.value) void viewActions.flush().then(() => (statusMessage.value = 'Forced online -- outbox flushed.'))
}

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

// Real, previously-undiscovered bug, found only by actually clicking "Set
// Amount" against a live schema for the first time (a real Playwright
// playbook was the first thing that ever had): every currently-
// orchestrated instance's schema (Vitals' PatientScreened, Meridian's
// ApplicantIdentity, ...) has real `required` fields ("SubjectId"/
// "SiteId"/"EligibilityStatus", ...) that a bare `{ Amount }` patch never
// carried -- this generic, domain-agnostic demo panel had never once
// successfully published against any of them; the server's own JSON
// Schema validation silently rejected it (400), leaving the outbox entry
// permanently Pending, retried forever.
//
// Two fixes, both TODO.md-tracked gaps closed this pass:
// 1. Merge the currently cached entity's own already-known fields
//    (guaranteed present -- `applyFollowedEvent` populates the cache
//    before `currentEntityId` is ever set to that entity,
//    useEntityViewActions.subscribeToEntity) in underneath the new
//    `Amount` value, satisfying whichever schema's `required` list
//    regardless of domain -- the same "Full" changeKind contract every
//    other real publisher in this codebase already sends a complete
//    snapshot for.
// 2. Re-case those merged keys to the schema's own REAL declared
//    property names first (`resolveEventTypeFieldCasing`, reusing the
//    Event Composer's own already-correct schema introspection,
//    `useEventComposer.ts`) -- the cached data only ever came through
//    GraphQL's own camelCased field names, but a `required` check is an
//    exact, case-sensitive string match against every real schema in
//    this repo's own PascalCase convention (`SubjectId`, not
//    `subjectId`). Fetched once per instance and cached in
//    `fieldCasing`, not on every dispatch -- a schema's shape doesn't
//    change between clicks.
const fieldCasing = ref<Map<string, string> | null>(null)
async function ensureFieldCasing(): Promise<Map<string, string>> {
  fieldCasing.value ??= await resolveEventTypeFieldCasing(config, config.eventType)
  return fieldCasing.value
}

async function submitAmountCommand(): Promise<void> {
  if (!currentEntityId.value) return
  const cached = entityCache.get(config.instanceId, currentEntityId.value)
  const casing = await ensureFieldCasing()
  const recased: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(cached?.data ?? {})) recased[casing.get(key.toLowerCase()) ?? key] = value
  await viewActions.dispatchCommand(currentEntityId.value, { ...recased, Amount: Number(amountInput.value) })
  statusMessage.value = effectivelyOnline.value ? 'Command dispatched.' : 'Offline -- queued in the local outbox.'
}

const pendingCount = computed(() => outbox.pendingFor(config.instanceId).length)
// A permanently-failed command (a 400/403 the server will never accept by
// mere retry, useOutboxStore.flush's own new distinction) is terminal, not
// pending -- shown as its own count rather than silently vanishing from
// pendingCount with no visible signal anything ever went wrong.
const failedCount = computed(() => outbox.failedFor(config.instanceId).length)

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
            <strong data-testid="connectivity-status">{{ effectivelyOnline ? 'online' : 'offline' }}</strong>
            — {{ pendingCount }} command(s) queued
            <span v-if="failedCount > 0" data-testid="failed-count"> — {{ failedCount }} failed (won't retry)</span>
            <n-button size="small" data-testid="force-offline" :disabled="!effectivelyOnline" @click="forceOfflineForDemo">Go Offline</n-button>
            <n-button size="small" data-testid="force-online" :disabled="effectivelyOnline" @click="forceOnlineForDemo">Go Online</n-button>
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
