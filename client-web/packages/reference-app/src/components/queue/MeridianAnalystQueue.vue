<script setup lang="ts">
import AuthorityQueue from './AuthorityQueue.vue'

// See VitalsPiQueue.vue's own comment on why domain specifics live in this
// thin wrapper, never in AuthorityQueue.vue. Unlike Vitals' IonmAlertRaised,
// SanctionsScreeningPerformed is registered with RequiredClaims: null and
// is an ordinary, immediately-"accepted" publish (MeridianWorkflowC.cs) --
// "needs analyst review" here is pure business data (MatchFound), never an
// AuthorityStatus concern, the deliberate asymmetry this domain-agnostic
// composable's own isPending seam exists to allow.
const props = defineProps<{ hostBaseUrl: string; authBaseUrl: string; appId: string }>()

function isPending(payload: Record<string, unknown>): boolean {
  return payload.matchFound === true
}
</script>

<template>
  <AuthorityQueue
    :host-base-url="props.hostBaseUrl"
    :auth-base-url="props.authBaseUrl"
    :app-id="props.appId"
    raiser-event-type="SanctionsScreeningPerformed"
    decision-client-id="meridian-analyst-client"
    decision-client-secret="meridian-analyst-client-secret"
    :is-pending="isPending"
    title="KYC Analyst Queue"
    reviewer-label="Reviewing as (Analyst)"
    reviewer-default="analyst-1"
    :chartable-fields="[{ field: 'matchConfidence', chartType: 'gauge', label: 'Match confidence' }]"
  />
</template>
