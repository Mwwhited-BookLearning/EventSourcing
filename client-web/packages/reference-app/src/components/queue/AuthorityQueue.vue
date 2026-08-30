<script setup lang="ts">
import { h, onMounted, onUnmounted, reactive, ref } from 'vue'
import { NButton, NCard, NDataTable, NInput, type DataTableColumns } from 'naive-ui'
import { usePendingAuthorityQueue, type PendingAuthorityItem } from '@eventstore/mvvm-client'
import GaugeChart from '../chart/GaugeChart.vue'

// ADR-100 -- the declarative presentation-type config this ADR
// establishes, kept out of AuthorityQueue.vue's own field knowledge the
// same way every other domain specific already is (raiserEventType,
// isPending, ...): the domain-specific wrapper (MeridianAnalystQueue.vue)
// supplies WHICH payload field is chartable and how, this component just
// renders whatever it's told. Deliberately narrow -- one chart type
// (`gauge`, for a 0.0-1.0 confidence-shaped field), not a general
// presentation-type schema speculatively covering types nothing needs yet.
export interface ChartableField {
  field: string
  chartType: 'gauge'
  label?: string
}

// "Domain Decision Queues" -- deliberately generic (docs/features/
// mvvm-client.md's own "never hardcodes a Vitals or Meridian field name"
// discipline, applied here for the first time to a bespoke, per-domain
// screen rather than the fully-generic Composer/Browser): every domain
// specific here comes in as a prop from a thin wrapper component
// (VitalsPiQueue.vue/MeridianAnalystQueue.vue), never hardcoded in this
// file itself.
const props = defineProps<{
  hostBaseUrl: string
  authBaseUrl: string
  appId: string
  raiserEventType: string
  decisionClientId: string
  decisionClientSecret: string
  isPending: (payload: Record<string, unknown>) => boolean
  title: string
  reviewerLabel: string
  reviewerDefault: string
  chartableFields?: ChartableField[]
}>()

const queue = usePendingAuthorityQueue({
  hostBaseUrl: props.hostBaseUrl,
  authBaseUrl: props.authBaseUrl,
  appId: props.appId,
  raiserEventType: props.raiserEventType,
  decisionClientId: props.decisionClientId,
  decisionClientSecret: props.decisionClientSecret,
  isPending: props.isPending,
})

const decidingActorId = ref(props.reviewerDefault)
const drafts = reactive<Record<string, { reason: string; meaning: string }>>({})
const statusByEventId = reactive<Record<string, string>>({})

function draftFor(eventId: string): { reason: string; meaning: string } {
  drafts[eventId] ??= { reason: '', meaning: '' }
  return drafts[eventId]
}

// A masked field (x-masking, ADR-009) arrives as the same three-way
// { value, masked, erased } wrapper object masking produces everywhere
// else -- a bare template-literal interpolation of that object renders
// the useless literal string "[object Object]" (found via a real
// playbook screenshot, the first one to ever exercise this queue against
// a payload with a masked field present -- MeridianWorkflowC's own
// MatchedName/MatchedListEntryId). EntityBrowser.vue's own summarize()
// already established the fix for this exact shape; mirrored here.
// ADR-100 -- a chartable field (rendered as its own gauge column, below)
// is excluded from the plain-text summary here, not shown in both places
// redundantly.
const chartableFieldNames = new Set((props.chartableFields ?? []).map((cf) => cf.field))

function summarize(payload: Record<string, unknown>): string {
  return Object.entries(payload)
    .filter(([key]) => key !== 'eventId' && !chartableFieldNames.has(key))
    .map(([key, value]) => `${key}: ${typeof value === 'object' && value !== null ? '[masked/complex]' : String(value)}`)
    .join(', ')
}

async function decide(eventId: string, decision: 'accepted' | 'rejected'): Promise<void> {
  const draft = draftFor(eventId)
  statusByEventId[eventId] = 'Publishing...'
  const result = await queue.decide(eventId, decision, decidingActorId.value, draft.reason, draft.meaning)
  statusByEventId[eventId] = result.ok
    ? `${result.steppedUp ? 'Stepped up authentication and recorded' : 'Recorded'} -- ${decision}`
    : 'Failed -- check connectivity, Meaning, and step-up requirements, then try again.'
}

onMounted(() => queue.subscribe())
onUnmounted(() => queue.stopSubscription())

// ADR-099 -- `n-data-table` with per-row render-function columns (Reason/
// Meaning inputs and Accept/Reject buttons are per-row interactive
// controls, not plain cell text), plus its own built-in pagination over
// this same already-fully-loaded queue array (see EntityBrowser.vue's
// identical note: this bounds render cost, it doesn't reduce what already
// crossed the wire via the underlying subscription).
const pagination = { pageSize: 10 } as const

// ADR-100 -- one column per configured chartable field, right after the
// plain-text summary. A non-numeric or missing value renders nothing
// rather than a broken chart -- a real, not hypothetical, case: a pending
// item's own payload can lack this field entirely if it arrived before
// the field was even part of the schema (ADR-back-compat is a routine
// concern this whole design already treats seriously elsewhere).
const chartColumns: DataTableColumns<PendingAuthorityItem> = (props.chartableFields ?? []).map((cf) => ({
  title: cf.label ?? cf.field,
  key: `chart-${cf.field}`,
  render: (row) => {
    const value = row.payload[cf.field]
    return typeof value === 'number' ? h(GaugeChart, { value, label: cf.label ?? cf.field }) : null
  },
}))

const columns: DataTableColumns<PendingAuthorityItem> = [
  { title: 'Item', key: 'summary', render: (row) => summarize(row.payload) },
  ...chartColumns,
  {
    title: 'Reason',
    key: 'reason',
    render: (row) =>
      h(NInput, {
        value: draftFor(row.eventId).reason,
        'onUpdate:value': (v: string) => (draftFor(row.eventId).reason = v),
      }),
  },
  {
    title: 'Reason for sign-off (Meaning) *',
    key: 'meaning',
    render: (row) =>
      h(NInput, {
        value: draftFor(row.eventId).meaning,
        inputProps: { 'data-testid': `queue-meaning-${row.eventId}` } as any,
        'onUpdate:value': (v: string) => (draftFor(row.eventId).meaning = v),
      }),
  },
  {
    title: '',
    key: 'actions',
    render: (row) => [
      h(
        NButton,
        { disabled: !draftFor(row.eventId).meaning.trim(), onClick: () => decide(row.eventId, 'accepted'), style: 'margin-right: 0.5rem' },
        { default: () => 'Accept' },
      ),
      h(NButton, { disabled: !draftFor(row.eventId).meaning.trim(), onClick: () => decide(row.eventId, 'rejected') }, { default: () => 'Reject' }),
    ],
  },
  {
    title: 'Status',
    key: 'status',
    render: (row) => (statusByEventId[row.eventId] ? h('span', { 'data-testid': `queue-status-${row.eventId}` }, statusByEventId[row.eventId]) : null),
  },
]
</script>

<template>
  <section :aria-label="title">
    <n-card>
      <h2>{{ title }}</h2>
      <label>
        {{ reviewerLabel }}
        <n-input v-model:value="decidingActorId" :input-props="({ 'data-testid': 'queue-reviewer-input' } as any)" />
      </label>

      <p v-if="queue.items.value.length === 0">Nothing pending review right now.</p>
      <n-data-table
        v-else
        data-testid="queue-list"
        :columns="columns"
        :data="queue.items.value"
        :pagination="pagination"
        :row-key="(row: PendingAuthorityItem) => row.eventId"
      />
    </n-card>
  </section>
</template>
