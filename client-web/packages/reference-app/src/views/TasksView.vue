<script setup lang="ts">
import MyTasksView from '../components/tasks/MyTasksView.vue'
import { useAppState } from '../appState'
import { queueDomain } from '../appConfig'

const { config } = useAppState()

// Same "one identity per real capability need" reasoning QueueView.vue's own
// VitalsPiQueue/MeridianAnalystQueue split already establishes -- this picks
// WHICH reviewer's claims the query runs as, MyTasksView.vue itself stays
// domain-agnostic. The standalone/"mvvm-demo" fallback (no queueDomain) uses
// follower-client -- a real, valid identity that simply holds none of the
// domain review claims, so it only ever sees a task with no RequiredClaim
// at all (there are none in this repo today), never a wrong answer.
const reviewerCredentials =
  queueDomain === 'vitals'
    ? { clientId: 'vitals-pi-client', clientSecret: 'vitals-pi-client-secret', scope: 'events:publish' }
    : queueDomain === 'meridian'
      ? { clientId: 'meridian-analyst-client', clientSecret: 'meridian-analyst-client-secret', scope: 'events:publish' }
      : { clientId: 'follower-client', clientSecret: 'follower-client-secret', scope: 'events:follow' }
</script>

<template>
  <MyTasksView
    :host-base-url="config.hostBaseUrl"
    :auth-base-url="config.authBaseUrl"
    :client-id="reviewerCredentials.clientId"
    :client-secret="reviewerCredentials.clientSecret"
    :scope="reviewerCredentials.scope"
  />
</template>
