<script setup lang="ts">
import { computed } from 'vue'
import { useEntityCacheStore } from '../../stores/entityCache'

// docs/features/mvvm-client.md, "Proving-Ground Application UX" -- no new
// server surface: mode: REPLAY (useEntityViewActions.ts's own subscribe())
// already accumulates one cache entry per distinct EntityId this instance
// has ever seen; this is purely a different VIEW over that same store.
const props = defineProps<{ instanceId: string }>()
const emit = defineEmits<{ select: [entityId: string] }>()

const entityCache = useEntityCacheStore()
const entities = computed(() => entityCache.listForInstance(props.instanceId))

function summarize(data: Record<string, unknown>): string {
  const entries = Object.entries(data).slice(0, 3)
  return entries.map(([key, value]) => `${key}: ${typeof value === 'object' && value !== null ? '[masked/complex]' : String(value)}`).join(', ')
}
</script>

<template>
  <section aria-label="Entity Browser">
    <h2>Entity Browser</h2>
    <p v-if="entities.length === 0">No entities cached yet for this instance.</p>
    <table v-else>
      <caption class="visually-hidden">Cached entities for this subscription instance</caption>
      <thead>
        <tr>
          <th scope="col">EntityId</th>
          <th scope="col">Summary</th>
          <th scope="col">AuthorityStatus</th>
          <th scope="col"></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="entry in entities" :key="entry.entityId">
          <td>{{ entry.entityId }}</td>
          <td>{{ summarize(entry.data) }}</td>
          <td>{{ entry.authorityStatus }}</td>
          <td><button type="button" @click="emit('select', entry.entityId)">View</button></td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<style scoped>
.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
</style>
