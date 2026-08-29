<script setup lang="ts">
import EventComposer from '../components/composer/EventComposer.vue'
import { useAppState } from '../appState'

const { config } = useAppState()
</script>

<template>
  <EventComposer :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />
</template>
