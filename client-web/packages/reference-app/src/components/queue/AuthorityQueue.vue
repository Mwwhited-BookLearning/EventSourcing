<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'
import { usePendingAuthorityQueue } from '@eventstore/mvvm-client'

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

function summarize(payload: Record<string, unknown>): string {
  return Object.entries(payload)
    .filter(([key]) => key !== 'eventId')
    .map(([key, value]) => `${key}: ${value}`)
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
</script>

<template>
  <section :aria-label="title">
    <h2>{{ title }}</h2>
    <label>
      {{ reviewerLabel }}
      <input v-model="decidingActorId" type="text" data-testid="queue-reviewer-input" />
    </label>

    <p v-if="queue.items.value.length === 0">Nothing pending review right now.</p>
    <ul v-else data-testid="queue-list">
      <li v-for="item in queue.items.value" :key="item.eventId" class="queue-item">
        <p>{{ summarize(item.payload) }}</p>
        <label>
          Reason
          <input v-model="draftFor(item.eventId).reason" type="text" />
        </label>
        <label>
          Reason for sign-off (Meaning) *
          <input v-model="draftFor(item.eventId).meaning" type="text" :data-testid="`queue-meaning-${item.eventId}`" />
        </label>
        <button type="button" :disabled="!draftFor(item.eventId).meaning.trim()" @click="decide(item.eventId, 'accepted')">Accept</button>
        <button type="button" :disabled="!draftFor(item.eventId).meaning.trim()" @click="decide(item.eventId, 'rejected')">Reject</button>
        <p v-if="statusByEventId[item.eventId]" :data-testid="`queue-status-${item.eventId}`">{{ statusByEventId[item.eventId] }}</p>
      </li>
    </ul>
  </section>
</template>
