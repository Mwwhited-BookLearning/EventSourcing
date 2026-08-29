<script setup lang="ts">
import { ref } from 'vue'
import { NButton, NCard } from 'naive-ui'
import { playbackAsOf, type PlaybackResult } from '@eventstore/mvvm-client'

// docs/features/lineage-export-and-playback.md, Screen 2 -- VCR-style
// bitemporal system-time playback, live against the real Gateway. [<]/[>]
// step to the immediately adjacent SequenceNumber (rewind/advance one
// arrival at a time); every position shown is a fresh playbackAsOf
// reconstruction, never a cached snapshot (ADR-068's own stated v1 scope
// -- periodic system-time snapshots are a named future optimization, not
// built here). This is the "connected to a live API" build target of the
// two ADR-068 §3 describes; OfflineBundleViewer.vue is the other.
const props = defineProps<{
  hostBaseUrl: string
  token: string
  entityId: string
  startingSequenceNumber: number
}>()

const asOfSequenceNumber = ref(props.startingSequenceNumber)
const result = ref<PlaybackResult | null>(null)
const loading = ref(false)
const notFound = ref(false)

async function loadCurrent(): Promise<void> {
  loading.value = true
  try {
    const loaded = await playbackAsOf(props.hostBaseUrl, props.token, props.entityId, asOfSequenceNumber.value)
    result.value = loaded
    notFound.value = loaded === null
  } finally {
    loading.value = false
  }
}

async function rewind(): Promise<void> {
  asOfSequenceNumber.value -= 1
  await loadCurrent()
}

async function advance(): Promise<void> {
  asOfSequenceNumber.value += 1
  await loadCurrent()
}

void loadCurrent()
</script>

<template>
  <section class="playback-control" aria-label="Bitemporal system-time playback">
    <n-card>
      <h2>Bitemporal Playback — {{ entityId }}</h2>
      <div class="controls">
        <span data-testid="sequence-number">SequenceNumber {{ asOfSequenceNumber }}</span>
        <n-button :disabled="loading" data-testid="rewind" @click="rewind">&lt;</n-button>
        <n-button :disabled="loading" data-testid="advance" @click="advance">&gt;</n-button>
      </div>
      <p v-if="notFound" data-testid="not-found">No reconstruction exists at or before this SequenceNumber.</p>
      <template v-else-if="result">
        <pre data-testid="playback-data">{{ result.data }}</pre>
        <p v-if="result.lateArrivalCorrectionShown" data-testid="late-arrival-notice">
          ⚠ A late-arriving correction is shown landing exactly here — never smoothed away.
        </p>
      </template>
    </n-card>
  </section>
</template>

<style scoped>
.playback-control .controls {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}
</style>
