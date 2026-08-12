<script setup lang="ts">
import AuthorityQueue from './AuthorityQueue.vue'

// A thin, domain-specific wrapper -- every Vitals-specific fact (which
// event type raises a pending item, which identity decides, what "pending"
// actually means for this domain) lives here, never in AuthorityQueue.vue
// itself. "pending_review" is IonmAlertRaised's own AuthorityStatus
// (ADR-042, set via Samples.Vitals.Simulator's ReviewPending: true) --
// this is a genuine non-authoritative-capture case, not a business flag,
// unlike Meridian's own MatchFound below.
const props = defineProps<{ hostBaseUrl: string; authBaseUrl: string; appId: string }>()

function isPending(payload: Record<string, unknown>): boolean {
  return payload.authorityStatus === 'pending_review'
}
</script>

<template>
  <AuthorityQueue
    :host-base-url="props.hostBaseUrl"
    :auth-base-url="props.authBaseUrl"
    :app-id="props.appId"
    raiser-event-type="IonmAlertRaised"
    decision-client-id="vitals-pi-client"
    decision-client-secret="vitals-pi-client-secret"
    :is-pending="isPending"
    title="Principal Investigator Queue"
    reviewer-label="Reviewing as (PI)"
    reviewer-default="pi-1"
  />
</template>
