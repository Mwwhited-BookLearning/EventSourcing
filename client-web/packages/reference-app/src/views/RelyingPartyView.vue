<script setup lang="ts">
import RelyingPartyAccessPanel from '../components/relyingParty/RelyingPartyAccessPanel.vue'
import { useAppState } from '../appState'

const { config } = useAppState()
</script>

<template>
  <RelyingPartyAccessPanel :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" :app-id="config.appId" />
</template>
