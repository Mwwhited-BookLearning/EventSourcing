<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ClientEntityCacheEntry } from '../../types'
import FlagRow from './FlagRow.vue'

// ADR-039's "small injected binding runtime" -- raw HTML+JS
// (`templateContent`), never precompiled, interpreted by this generic
// renderer at runtime. Deliberately minimal (one-way `{{ field }}`
// interpolation + a `data-command-field`/`data-command-value-from`
// attribute convention for dispatching a command), not a full templating
// engine -- exactly what the ADR's own Consequences call for ("needing zero
// extra machinery" beyond what the web platform gives for free). The
// flag-rendering convention below is the SAME FlagRow component
// GenericFallbackView uses -- one implementation, not a template-specific
// second one.
const props = defineProps<{
  templateContent: string
  entry: ClientEntityCacheEntry
}>()

const emit = defineEmits<{ command: [fieldPath: string, value: string] }>()

const container = ref<HTMLDivElement | null>(null)

function interpolate(template: string, entry: ClientEntityCacheEntry): string {
  return template.replace(/\{\{\s*([\w.]+)\s*\}\}/g, (_match, path: string) => {
    if (path === 'entityId') return entry.entityId
    if (path === 'entityType') return entry.entityType
    const value = entry.data[path]
    return value === undefined || value === null ? '' : String(value)
  })
}

function render(): void {
  if (!container.value) return
  container.value.innerHTML = interpolate(props.templateContent, props.entry)
}

function handleClick(event: Event): void {
  const target = (event.target as HTMLElement | null)?.closest('[data-command-field]') as HTMLElement | null
  if (!target || !container.value) return
  const fieldPath = target.getAttribute('data-command-field')
  const valueFromSelector = target.getAttribute('data-command-value-from')
  if (!fieldPath || !valueFromSelector) return
  const input = container.value.querySelector<HTMLInputElement>(valueFromSelector)
  if (!input) return
  emit('command', fieldPath, input.value)
}

onMounted(() => {
  render()
  container.value?.addEventListener('click', handleClick)
})
onBeforeUnmount(() => container.value?.removeEventListener('click', handleClick))
watch(() => [props.templateContent, props.entry], render, { deep: true })
</script>

<template>
  <section aria-label="Entity (ViewDefinition-rendered)">
    <div ref="container" data-testid="template-container"></div>
    <FlagRow :conflict-flag="entry.conflictFlag" :late-arrival-flag="entry.lateArrivalFlag" :authority-status="entry.authorityStatus" />
  </section>
</template>
