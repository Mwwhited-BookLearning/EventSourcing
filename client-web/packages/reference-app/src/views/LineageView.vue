<script setup lang="ts">
import LineageExportAndPlaybackPanel from '../components/playback/LineageExportAndPlaybackPanel.vue'
import { useAppState } from '../appState'

const { config } = useAppState()
</script>

<template>
  <LineageExportAndPlaybackPanel :host-base-url="config.hostBaseUrl" :auth-base-url="config.authBaseUrl" />
</template>
