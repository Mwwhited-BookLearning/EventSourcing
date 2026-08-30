<script setup lang="ts">
import EntityBrowser from '../components/entity/EntityBrowser.vue'
import { useAppState } from '../appState'

const { config, selectFromBrowser } = useAppState()
</script>

<template>
  <EntityBrowser
    :instance-id="config.instanceId"
    :host-base-url="config.hostBaseUrl"
    :auth-base-url="config.authBaseUrl"
    :app-id="config.appId"
    :entity-type="config.entityType"
    :client-id="config.clientId"
    :client-secret="config.clientSecret"
    :scope="config.scope"
    @select="selectFromBrowser"
  />
</template>
