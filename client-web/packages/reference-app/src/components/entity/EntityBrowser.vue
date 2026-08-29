<script setup lang="ts">
import { computed, h, ref } from 'vue'
import { NButton, NDataTable, NInput, type DataTableColumns } from 'naive-ui'
import { useEntityCacheStore, type ClientEntityCacheEntry } from '@eventstore/mvvm-client'

// docs/features/mvvm-client.md, "Proving-Ground Application UX" -- no new
// server surface: mode: REPLAY (useEntityViewActions.ts's own subscribe())
// already accumulates one cache entry per distinct EntityId this instance
// has ever seen; this is purely a different VIEW over that same store.
const props = defineProps<{ instanceId: string }>()
const emit = defineEmits<{ select: [entityId: string] }>()

const entityCache = useEntityCacheStore()
const entities = computed(() => entityCache.listForInstance(props.instanceId))

// ADR-099 -- found by actually running a real playbook against a live
// AppHost, not assumed: pagination alone makes a specific, already-known
// EntityId (e.g. the seed data's own continuity subject) UNDISCOVERABLE
// once a long-running simulator has pushed enough newer entities in front
// of it to land it past page 1, with no way back to it. A simple
// client-side filter (over this same already-loaded array -- still no new
// server query) is the minimum fix that makes pagination usable rather
// than just smaller.
const filterText = ref('')
const filteredEntities = computed(() => {
  const needle = filterText.value.trim().toLowerCase()
  if (!needle) return entities.value
  return entities.value.filter((entry) => entry.entityId.toLowerCase().includes(needle) || summarize(entry.data).toLowerCase().includes(needle))
})

function summarize(data: Record<string, unknown>): string {
  const entries = Object.entries(data).slice(0, 3)
  return entries.map(([key, value]) => `${key}: ${typeof value === 'object' && value !== null ? '[masked/complex]' : String(value)}`).join(', ')
}

// ADR-099 -- `n-data-table`'s own built-in pagination, over this same
// already-fully-loaded in-memory array (TODO.md tracks the separate,
// bigger question of a real paged server query -- this only bounds
// render/DOM cost per page, it does not reduce what already crossed the
// wire via the REPLAY subscription that fills this cache).
const pagination = { pageSize: 10 } as const

const columns: DataTableColumns<ClientEntityCacheEntry> = [
  { title: 'EntityId', key: 'entityId' },
  { title: 'Summary', key: 'summary', render: (row) => summarize(row.data) },
  { title: 'AuthorityStatus', key: 'authorityStatus' },
  {
    title: '',
    key: 'actions',
    render: (row) => h(NButton, { size: 'small', onClick: () => emit('select', row.entityId) }, { default: () => 'View' }),
  },
]
</script>

<template>
  <section aria-label="Entity Browser">
    <h2>Entity Browser</h2>
    <p v-if="entities.length === 0">No entities cached yet for this instance.</p>
    <template v-else>
      <n-input v-model:value="filterText" placeholder="Filter by EntityId or summary…" :input-props="({ 'data-testid': 'entity-browser-filter' } as any)" />
      <n-data-table :columns="columns" :data="filteredEntities" :pagination="pagination" :row-key="(row: ClientEntityCacheEntry) => row.entityId" />
    </template>
  </section>
</template>
