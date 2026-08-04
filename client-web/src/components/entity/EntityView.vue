<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useEntityCacheStore } from '../../stores/entityCache'
import { useViewDefinitionsStore } from '../../stores/viewDefinitions'
import TemplateRenderer from './TemplateRenderer.vue'
import GenericFallbackView from './GenericFallbackView.vue'
import type { useEntityViewActions } from '../../composables/useEntityViewActions'

// docs/features/mvvm-client.md's "rendering a ViewDefinition, with generic
// fallback" sequence diagram, realized directly: read the cache (offline-
// safe, no network required to just render), look up a ViewDefinition for
// this EntityType, and render either the template-backed view or the
// generic fallback -- never a blank/failed render either way.
const props = defineProps<{
  entityId: string
  instanceId: string
  entityType: string
  viewActions: ReturnType<typeof useEntityViewActions>
}>()

const entityCache = useEntityCacheStore()
const viewDefinitions = useViewDefinitionsStore()

const entry = computed(() => entityCache.get(props.instanceId, props.entityId))
const viewDefinition = computed(() => viewDefinitions.get(props.entityType, 'Detail'))

onMounted(() => {
  void props.viewActions.loadViewDefinition('Detail')
})

async function handleCommand(fieldPath: string, value: string): Promise<void> {
  await props.viewActions.dispatchCommand(props.entityId, { [fieldPath]: value })
}

async function handleRetry(): Promise<void> {
  await props.viewActions.flush()
}
</script>

<template>
  <div v-if="!entry">Loading…</div>
  <TemplateRenderer
    v-else-if="viewDefinition"
    :template-content="viewDefinition.templateContent"
    :entry="entry"
    @command="handleCommand"
  />
  <GenericFallbackView v-else :entry="entry" @retry="handleRetry" />
</template>
