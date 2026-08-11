<script setup lang="ts">
import { ref, watchEffect } from 'vue'
import { parseNdjson, type LineageExportBundle } from '../../playback/bundle'
import { verifyBundle, type BundleVerificationResult } from '../../playback/verifyBundle'

// docs/features/lineage-export-and-playback.md, Screen 3 -- the self-
// contained offline player's own verification-result screen. Runs
// unchanged in BOTH build targets ADR-068 §3 describes: the ordinary
// connected app (bundleNdjson supplied via a file picker, for reviewing a
// downloaded export without a second tool) and the vite-plugin-singlefile
// offline build (bundleNdjson supplied already-embedded at build time --
// see offline-player/main.ts). No masking/claims logic runs here at all
// -- enforcement already happened once, at export time; every field
// renders exactly as it appears in the bundle, masked/erased branches
// included verbatim (ADR-068's own "no bypass, no second enforcement
// point" rule).
const props = defineProps<{
  bundleNdjson: string
}>()

const bundle = ref<LineageExportBundle | null>(null)
const verification = ref<BundleVerificationResult | null>(null)
const parseError = ref<string | null>(null)
const showEventList = ref(false)

watchEffect(async () => {
  parseError.value = null
  verification.value = null
  try {
    const parsed = parseNdjson(props.bundleNdjson)
    bundle.value = parsed
    verification.value = await verifyBundle(parsed)
  } catch (error) {
    bundle.value = null
    parseError.value = error instanceof Error ? error.message : String(error)
  }
})
</script>

<template>
  <section class="offline-player" aria-label="Offline lineage export player">
    <h2 v-if="bundle">Offline Player — {{ bundle.manifest.entityId }}</h2>
    <p v-if="parseError" data-testid="parse-error">{{ parseError }}</p>
    <template v-else-if="verification">
      <p v-if="verification.fullyVerified" data-testid="verdict-full">
        ✔ Fully independently verified — manifest hash matches, no masked or erased fields.
      </p>
      <p v-else-if="verification.manifestHashVerified" data-testid="verdict-partial">
        ⚠ Verified except {{ verification.maskedFieldCount + verification.erasedFieldCount }} masked/erased field(s) —
        manifest hash intact; chain linkage unaffected by masking.
      </p>
      <p v-else data-testid="verdict-failed">
        ✘ Manifest hash does not match a recomputation over this bundle's own ChainHash values — this bundle may have
        been tampered with or corrupted since export.
      </p>
      <table v-if="verification.maskedFieldCount > 0 || verification.erasedFieldCount > 0" data-testid="masked-summary">
        <tbody>
          <tr>
            <td>Masked fields</td>
            <td>{{ verification.maskedFieldCount }}</td>
          </tr>
          <tr>
            <td>Erased fields</td>
            <td>{{ verification.erasedFieldCount }}</td>
          </tr>
        </tbody>
      </table>
      <button type="button" data-testid="toggle-event-list" @click="showEventList = !showEventList">
        {{ showEventList ? 'Hide' : 'View' }} full event list
      </button>
      <table v-if="showEventList && bundle" data-testid="event-list">
        <thead>
          <tr>
            <th>SequenceNumber</th>
            <th>EventType</th>
            <th>OccurredAt</th>
            <th>LateArrivalFlag</th>
            <th>Payload</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="event in bundle.events" :key="event.eventId">
            <td>{{ event.sequenceNumber }}</td>
            <td>{{ event.eventType }}</td>
            <td>{{ event.occurredAt }}</td>
            <td>{{ event.lateArrivalFlag }}</td>
            <td><code>{{ event.payload }}</code></td>
          </tr>
        </tbody>
      </table>
    </template>
  </section>
</template>

<style scoped>
.offline-player table {
  border-collapse: collapse;
  margin: 0.5rem 0;
}
.offline-player td,
.offline-player th {
  padding: 0.25rem 0.75rem;
  border-bottom: 1px solid var(--duplex-border, #eee);
  text-align: left;
}
</style>
