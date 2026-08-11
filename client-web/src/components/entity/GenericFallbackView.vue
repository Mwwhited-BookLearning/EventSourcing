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
    <!-- ADR-073's own exit criterion calls for a manual screen-reader pass
         specifically confirming this fallback is fully navigable, not
         merely visually present -- `<th scope="row">` for the property
         name (rather than a plain `<td>`) is exactly what makes a screen
         reader announce each value together with its own label
         ("carrier: UPS", not two anonymous cells); axe-core's automated
         ruleset doesn't flag a headerless 2-column table as a violation
         (nothing automated can tell whether a given table's first column
         is semantically a label), so this was found by reasoning about
         real screen-reader behavior directly, not by a tool. -->
    <table>
      <caption class="visually-hidden">Entity properties</caption>
      <tbody>
        <tr v-for="row in rows" :key="row.name">
          <th scope="row">{{ row.name }}</th>
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
.generic-fallback td,
.generic-fallback th {
  padding: 0.25rem 0.75rem;
  border-bottom: 1px solid var(--duplex-border, #eee);
}
.generic-fallback th {
  font-weight: normal;
  text-align: left;
}
/* Standard screen-reader-only pattern: announced by assistive tech,
   never rendered visually -- this table's purpose is already clear
   sighted from context (the section's own aria-label + heading), so
   the caption exists for screen-reader users specifically, not for
   everyone. */
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
}
</style>
