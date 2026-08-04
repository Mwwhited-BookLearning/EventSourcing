<script setup lang="ts">
import { computed } from 'vue'
import type { ClientEntityCacheEntry } from '../../types'
import FlagRow from './FlagRow.vue'

// ADR-039's own required fallback: "no view definition for an entity
// type/version renders a generic property-list view ... rather than
// failing to display the entity at all." Every row here comes from the
// same Data/Extensions bag a registered ViewDefinition would also receive
// via the bridge (TemplateRenderer.vue) -- this component just lists
// properties by name instead of binding them into a template-defined
// layout, matching docs/features/mvvm-client.md's own Salt mockup.
const props = defineProps<{
  entry: ClientEntityCacheEntry
}>()

const emit = defineEmits<{ retry: [] }>()

const rows = computed(() => [
  ...Object.entries(props.entry.data).map(([name, value]) => ({ name, value, fromExtensions: false })),
  ...Object.entries(props.entry.extensions).map(([name, value]) => ({ name, value, fromExtensions: true })),
])
</script>

<template>
  <section class="generic-fallback" aria-label="Entity (generic fallback view)">
    <h2>{{ entry.entityType }} {{ entry.entityId }} (no registered ViewDefinition -- generic fallback)</h2>
    <table>
      <tbody>
        <tr v-for="row in rows" :key="row.name">
          <td>{{ row.name }}</td>
          <td>
            {{ row.value }}
            <em v-if="row.fromExtensions">(Extensions)</em>
          </td>
        </tr>
      </tbody>
    </table>
    <FlagRow :conflict-flag="entry.conflictFlag" :late-arrival-flag="entry.lateArrivalFlag" :authority-status="entry.authorityStatus" />
    <button type="button" @click="emit('retry')">Retry sync</button>
  </section>
</template>

<style scoped>
.generic-fallback table {
  border-collapse: collapse;
  margin-bottom: 0.75rem;
}
.generic-fallback td {
  padding: 0.25rem 0.75rem;
  border-bottom: 1px solid var(--duplex-border, #eee);
}
</style>
