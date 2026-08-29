<script setup lang="ts">
import { computed } from 'vue'
import { NButton, NCard } from 'naive-ui'
import type { ClientEntityCacheEntry } from '@eventstore/mvvm-client'
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
  <section aria-label="Entity (generic fallback view)">
  <!-- `aria-label` moved to this plain `<section>`, not `n-card`'s own
       root `<div>` -- found via this file's own a11y.spec.ts (ADR-073,
       run for real): `aria-label` on a `<div>` with no ARIA role is a
       real axe-core "aria-prohibited-attr" finding. A `<section>` with
       an accessible name is an established landmark role; the div
       underneath doesn't need one of its own. -->
  <n-card class="generic-fallback">
    <!-- A real `<h2>`, not n-card's own `title` prop -- found via this
         file's own a11y.spec.ts (ADR-073, run for real, not assumed):
         n-card's `title`/`header` content renders wrapped in
         `role="heading"` with no `aria-level`, a genuine axe-core
         "aria-required-attr" critical violation in this Naive UI version.
         A plain heading element carries a correct implicit level with no
         custom role needed, so it sidesteps the bug entirely. -->
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
         real screen-reader behavior directly, not by a tool. ADR-099 --
         kept as a plain table (not swapped for `n-descriptions`) rather
         than gamble this specific, already-hard-won accessibility
         property against a component whose exact DOM semantics for an
         arbitrary, dynamically-sized property list aren't verified in
         this repo; the surrounding chrome (n-card) is Naive UI, this
         table's own markup is deliberately unchanged. -->
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
    <n-button @click="emit('retry')">Retry sync</n-button>
  </n-card>
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
  text-align: start; /* ADR-087 -- a CSS Logical Property, flips with dir="rtl" instead of staying pinned physically left */
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
