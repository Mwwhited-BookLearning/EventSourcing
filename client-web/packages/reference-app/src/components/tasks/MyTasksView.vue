<script setup lang="ts">
import { h, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NCard, NDataTable, NTag, type DataTableColumns } from 'naive-ui'
import { useMyTasks, type PendingTask } from '@eventstore/mvvm-client'

// Domain-agnostic on purpose -- ADR-101's whole point is one cross-domain
// list, so this never branches on AppId itself (TasksView.vue, the thin
// router-level wrapper, is the one place that picks which reviewer
// identity's claims this instance queries with).
const props = defineProps<{
  hostBaseUrl: string
  authBaseUrl: string
  clientId: string
  clientSecret: string
  scope: string
}>()

const router = useRouter()
const myTasks = useMyTasks({
  hostBaseUrl: props.hostBaseUrl,
  authBaseUrl: props.authBaseUrl,
  clientId: props.clientId,
  clientSecret: props.clientSecret,
  scope: props.scope,
})

onMounted(() => myTasks.startPolling())
onUnmounted(() => myTasks.stopPolling())

function domainLabel(appId: string): string {
  return appId === 'trial1' ? 'Vitals' : appId === 'kyc' ? 'Meridian' : appId
}

// The task list can be out of order (it's just a query) -- n-data-table's
// own built-in pagination bounds render cost, same reasoning AuthorityQueue.vue
// already applies, no ORDER BY anywhere in this chain either.
const pagination = { pageSize: 10 } as const

const columns: DataTableColumns<PendingTask> = [
  { title: 'Description', key: 'description' },
  { title: 'Domain', key: 'appId', render: (row) => h(NTag, { size: 'small' }, { default: () => domainLabel(row.appId) }) },
  { title: 'Flow', key: 'flowName' },
  { title: 'Entity', key: 'entityId' },
  { title: 'Raised', key: 'raisedAt', render: (row) => new Date(row.raisedAt).toLocaleString() },
  {
    title: '',
    key: 'actions',
    // The decision UI itself (Accept/Reject, step-up retry) stays on /queue
    // (AuthorityQueue.vue) -- this list only discovers work, it never
    // publishes a decision, see ADR-101's own Consequences on this split.
    render: (row) => h(NButton, { 'data-testid': `task-open-${row.key}`, onClick: () => router.push('/queue') }, { default: () => 'Open' }),
  },
]
</script>

<template>
  <section aria-label="My Tasks">
    <n-card>
      <h2>My Tasks</h2>
      <n-button data-testid="tasks-refresh" :loading="myTasks.loading.value" style="margin-bottom: 1rem" @click="myTasks.refresh">Refresh</n-button>
      <p v-if="myTasks.error.value" data-testid="tasks-error">{{ myTasks.error.value }}</p>
      <p v-else-if="myTasks.tasks.value.length === 0">Nothing pending right now.</p>
      <n-data-table
        v-else
        data-testid="tasks-list"
        :columns="columns"
        :data="myTasks.tasks.value"
        :pagination="pagination"
        :row-key="(row: PendingTask) => row.key"
      />
    </n-card>
  </section>
</template>
