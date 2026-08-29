<script setup lang="ts">
import { h, ref, watchEffect } from 'vue'
import { NAlert, NButton, NCard, NDataTable, type DataTableColumns } from 'naive-ui'
import { parseNdjson, type LineageExportBundle, verifyBundle, type BundleVerificationResult, type ExportedEventLine } from '@eventstore/mvvm-client'

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

const eventListColumns: DataTableColumns<ExportedEventLine> = [
  { title: 'SequenceNumber', key: 'sequenceNumber' },
  { title: 'EventType', key: 'eventType' },
  { title: 'OccurredAt', key: 'occurredAt' },
  { title: 'LateArrivalFlag', key: 'lateArrivalFlag', render: (row) => String(row.lateArrivalFlag) },
  { title: 'Payload', key: 'payload', render: (row) => h('code', row.payload) },
]

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
    <n-card>
      <h2 v-if="bundle">Offline Player — {{ bundle.manifest.entityId }}</h2>
      <n-alert v-if="parseError" type="error" data-testid="parse-error">{{ parseError }}</n-alert>
      <template v-else-if="verification">
        <n-alert v-if="verification.fullyVerified" type="success" data-testid="verdict-full">
          ✔ Fully independently verified — manifest hash matches, no masked or erased fields.
        </n-alert>
        <n-alert v-else-if="verification.manifestHashVerified" type="warning" data-testid="verdict-partial">
          ⚠ Verified except {{ verification.maskedFieldCount + verification.erasedFieldCount }} masked/erased field(s) —
          manifest hash intact; chain linkage unaffected by masking.
        </n-alert>
        <n-alert v-else type="error" data-testid="verdict-failed">
          ✘ Manifest hash does not match a recomputation over this bundle's own ChainHash values — this bundle may have
          been tampered with or corrupted since export.
        </n-alert>
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
        <n-button data-testid="toggle-event-list" @click="showEventList = !showEventList">
          {{ showEventList ? 'Hide' : 'View' }} full event list
        </n-button>
        <n-data-table
          v-if="showEventList && bundle"
          data-testid="event-list"
          :columns="eventListColumns"
          :data="bundle.events"
          :row-key="(row: ExportedEventLine) => row.eventId"
        />
      </template>
    </n-card>
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
  text-align: start; /* ADR-087 -- a CSS Logical Property, flips with dir="rtl" instead of staying pinned physically left */
}
</style>
