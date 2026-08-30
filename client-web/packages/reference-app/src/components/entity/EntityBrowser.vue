<script setup lang="ts">
import { h, onMounted, ref, watch } from 'vue'
import { NButton, NDataTable, NInput, type DataTableColumns } from 'naive-ui'
import { useEntityBrowserQuery, type BrowsedEntityRow } from '@eventstore/mvvm-client'

// TODO.md, "Data grids: a real paged server query" -- this component's
// own data source, replacing the previous "every entity this instance's
// REPLAY subscription has ever seen, accumulated into useEntityCacheStore"
// pattern (ADR-099's own client-side-only pagination over that same
// array). Detail view's real-time cache (useEntityCacheStore) is
// unaffected -- this is additive, a different, server-paged data source
// for THIS view specifically, not a replacement for that mechanism.
const props = defineProps<{
  instanceId: string
  hostBaseUrl: string
  authBaseUrl: string
  appId: string
  entityType: string
  clientId: string
  clientSecret: string
  scope: string
}>()
const emit = defineEmits<{ select: [entityId: string] }>()

const browserQuery = useEntityBrowserQuery({
  appId: props.appId, entityType: props.entityType, hostBaseUrl: props.hostBaseUrl,
  authBaseUrl: props.authBaseUrl, clientId: props.clientId, clientSecret: props.clientSecret, scope: props.scope,
})

const rows = ref<BrowsedEntityRow[]>([])
const totalCount = ref(0)
const loading = ref(true)
const pageIndex = ref(0) // 0-based; n-data-table's own `page` prop is 1-based
const pageSize = 10

// A real server-side WHERE clause (EntityQueryTypeModule.cs's own
// "contains" argument), not a client-side re-filter of an already-loaded
// array -- narrows the actual matching set on the server, so a known
// EntityId (e.g. the seed data's own continuity subject) stays reachable
// regardless of which page it would otherwise land on. Debounced --
// every keystroke would otherwise fire its own round trip.
const filterText = ref('')
let filterDebounceHandle: ReturnType<typeof setTimeout> | undefined

// The initial unfiltered load (onMounted, below) and the debounced
// filtered load (fired 300ms after the user types) are two independent
// requests -- nothing guarantees they resolve in the order they were
// SENT. Found only by actually running a Playwright playbook against a
// live AppHost, not assumed: whichever response happens to arrive LAST
// wins and overwrites `rows`/`totalCount`, so an unlucky ordering could
// let the stale unfiltered page clobber the correct filtered one after
// it briefly rendered, hanging every "wait for the filtered row count"
// assertion until its own timeout. A monotonically-increasing request
// generation, checked before applying a response, is the standard fix --
// a response is only applied if it's still the most recently STARTED
// request when it comes back, regardless of arrival order.
let latestRequestGeneration = 0

async function loadPage(): Promise<void> {
  const thisRequestGeneration = ++latestRequestGeneration
  loading.value = true
  try {
    const result = await browserQuery.fetchPage(pageIndex.value, pageSize, filterText.value.trim() || undefined)
    if (thisRequestGeneration !== latestRequestGeneration) return // superseded by a newer request while this one was in flight
    rows.value = result.rows
    totalCount.value = result.totalCount
  } finally {
    if (thisRequestGeneration === latestRequestGeneration) loading.value = false
  }
}

onMounted(loadPage)

watch(filterText, () => {
  clearTimeout(filterDebounceHandle)
  filterDebounceHandle = setTimeout(() => {
    pageIndex.value = 0 // a new filter always restarts from page 1 -- the old page index may not even exist in the narrowed set
    void loadPage()
  }, 300)
})

function summarize(data: Record<string, unknown>): string {
  const entries = Object.entries(data).slice(0, 3)
  return entries.map(([key, value]) => `${key}: ${typeof value === 'object' && value !== null ? '[masked/complex]' : String(value)}`).join(', ')
}

const pagination = {
  page: 1,
  pageSize,
  itemCount: 0,
  onUpdatePage: (page: number) => {
    pageIndex.value = page - 1
    void loadPage()
  },
}

const columns: DataTableColumns<BrowsedEntityRow> = [
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
    <n-input v-model:value="filterText" placeholder="Filter by EntityId…" :input-props="({ 'data-testid': 'entity-browser-filter' } as any)" />
    <p v-if="!loading && rows.length === 0">No matching entities.</p>
    <n-data-table
      v-else
      :columns="columns"
      :data="rows"
      :loading="loading"
      :pagination="{ ...pagination, page: pageIndex + 1, itemCount: totalCount }"
      :remote="true"
      :row-key="(row: BrowsedEntityRow) => row.entityId"
    />
  </section>
</template>
