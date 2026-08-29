<script setup lang="ts">
import { ref } from 'vue'
import { NAlert, NButton, NCard, NFormItem, NInput, NInputNumber } from 'naive-ui'
import { useLineageExportAndPlayback } from '@eventstore/mvvm-client'
import OfflineBundleViewer from './OfflineBundleViewer.vue'
import BitemporalPlaybackControl from './BitemporalPlaybackControl.vue'

// ADR-068 "Lineage Export & Bitemporal Playback" -- the first client-web
// UI surface for it (Vitals' own Workflow C, Trial Data Export and
// Subject Rights, TODO.md). Mirrors docs/domains/clinical-trials-device-
// telemetry/features/trial-data-export-and-subject-rights.md's own Salt
// mockup exactly: one Entity ID field feeds an "Export Lineage Bundle"
// button (the produced NDJSON bundle rendered directly on this same
// screen via the already-built OfflineBundleViewer.vue, never a second
// page) and a separate "System-Time Playback" control (an As-of
// SequenceNumber field + Play, handing off to the already-built
// BitemporalPlaybackControl.vue). Both halves are ordinary reads
// (events:lineage:read, follower-client) -- this panel is domain-agnostic
// like Detail/Browse/Compose, not gated to a specific AppId.
const props = defineProps<{ hostBaseUrl: string; authBaseUrl: string }>()

const lineage = useLineageExportAndPlayback({ hostBaseUrl: props.hostBaseUrl, authBaseUrl: props.authBaseUrl })

const exportEntityId = ref('')
const exportInProgress = ref(false)
const exportError = ref('')
const bundleNdjson = ref<string | null>(null)

const playbackEntityId = ref('')
const startingSequenceNumber = ref(0)
const playbackToken = ref<string | null>(null)
const playbackStarted = ref(false)

async function runExport(): Promise<void> {
  exportInProgress.value = true
  exportError.value = ''
  bundleNdjson.value = null
  const result = await lineage.exportBundle(exportEntityId.value)
  exportInProgress.value = false
  if (result.ok) {
    bundleNdjson.value = result.bundleNdjson!
  } else {
    exportError.value = result.error ?? 'Export failed.'
  }
}

async function startPlayback(): Promise<void> {
  playbackToken.value = await lineage.getToken()
  playbackStarted.value = true
}
</script>

<template>
  <section aria-label="Lineage Export and Bitemporal Playback">
    <h2>Lineage Export and Bitemporal Playback</h2>

    <!-- aria-label on the plain <section>, not n-card's own root <div> --
         a <div> with no ARIA role doesn't support aria-label (a real
         axe-core finding, verified against GenericFallbackView.vue). -->
    <section aria-label="Export Lineage Bundle">
      <n-card>
        <h3>Export Lineage Bundle</h3>
        <!-- aria-label, not label-for -- this Naive UI version's
             NFormItem never actually wires a `for` association (see
             RelyingPartyAccessPanel.vue's own note). -->
        <n-form-item label="Entity ID">
          <n-input v-model:value="exportEntityId" :input-props="({ 'aria-label': 'Entity ID', 'data-testid': 'export-entity-id' } as any)" />
        </n-form-item>
        <n-button :disabled="!exportEntityId || exportInProgress" data-testid="export-button" @click="runExport">Export Lineage Bundle</n-button>
        <n-alert v-if="exportError" type="error" data-testid="export-error" role="alert">{{ exportError }}</n-alert>
        <OfflineBundleViewer v-if="bundleNdjson" :bundle-ndjson="bundleNdjson" />
      </n-card>
    </section>

    <section aria-label="System-Time Playback">
      <n-card>
        <h3>System-Time Playback</h3>
        <n-form-item label="Entity ID">
          <n-input v-model:value="playbackEntityId" :input-props="({ 'aria-label': 'Entity ID', 'data-testid': 'playback-entity-id' } as any)" />
        </n-form-item>
        <n-form-item label="As of SequenceNumber">
          <n-input-number v-model:value="startingSequenceNumber" :input-props="({ 'aria-label': 'As of SequenceNumber', 'data-testid': 'playback-starting-sequence-number' } as any)" />
        </n-form-item>
        <n-button :disabled="!playbackEntityId" data-testid="playback-play" @click="startPlayback">Play</n-button>
        <BitemporalPlaybackControl
          v-if="playbackStarted && playbackToken"
          :host-base-url="props.hostBaseUrl"
          :token="playbackToken"
          :entity-id="playbackEntityId"
          :starting-sequence-number="startingSequenceNumber"
        />
      </n-card>
    </section>
  </section>
</template>
