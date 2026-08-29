<script setup lang="ts">
import EntityBrowser from '../components/entity/EntityBrowser.vue'
import { useAppState } from '../appState'

const { config, selectFromBrowser } = useAppState()
</script>

<template>
  <EntityBrowser :instance-id="config.instanceId" @select="selectFromBrowser" />
</template>
