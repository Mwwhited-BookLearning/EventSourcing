<script setup lang="ts">
import VitalsPiQueue from '../components/queue/VitalsPiQueue.vue'
import MeridianAnalystQueue from '../components/queue/MeridianAnalystQueue.vue'
import { useAppState } from '../appState'
import { queueDomain } from '../appConfig'

const { config } = useAppState()
</script>

<template>
  <VitalsPiQueue v-if="queueDomain === 'vitals'" :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />
  <MeridianAnalystQueue v-else-if="queueDomain === 'meridian'" :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />
</template>
