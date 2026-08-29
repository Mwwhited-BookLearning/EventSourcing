<script setup lang="ts">
import { ref, computed } from 'vue'
import { NButton, NCard, NCheckbox, NInput } from 'naive-ui'
import { useEventComposer, type ComposerFormField, type ComposerRequiredSignature, type EventTypeSummary } from '@eventstore/mvvm-client'

const props = defineProps<{
  hostBaseUrl: string
  authBaseUrl: string
  appId: string
}>()

const composer = useEventComposer({ hostBaseUrl: props.hostBaseUrl, authBaseUrl: props.authBaseUrl, appId: props.appId })

const eventTypes = ref<EventTypeSummary[]>([])
const selected = ref<EventTypeSummary | null>(null)
const fields = ref<ComposerFormField[]>([])
const requiredSignature = ref<ComposerRequiredSignature | null>(null)
const formValues = ref<Record<string, string | boolean>>({})
const meaning = ref('')
const statusMessage = ref('')
const loaded = ref(false)

async function loadEventTypes(): Promise<void> {
  eventTypes.value = await composer.listEventTypes()
  loaded.value = true
}

async function selectEventType(summary: EventTypeSummary): Promise<void> {
  selected.value = summary
  statusMessage.value = ''
  meaning.value = ''
  const detail = await composer.getEventTypeDetail(summary.name, summary.version)
  fields.value = detail.fields
  requiredSignature.value = detail.requiredSignature
  formValues.value = {}
  for (const field of fields.value) if (field.type === 'boolean') formValues.value[field.name] = false
}

const canPublish = computed(
  () =>
    selected.value !== null &&
    fields.value.filter((f) => f.required && f.editable).every((f) => formValues.value[f.name]) &&
    (requiredSignature.value === null || meaning.value.trim() !== ''),
)

function coerceValue(field: ComposerFormField, raw: string | boolean): unknown {
  if (field.type === 'boolean') return Boolean(raw)
  if (field.type === 'number' || field.type === 'integer') return Number(raw)
  if (field.type === 'array') return String(raw).split(',').map((v) => v.trim()).filter(Boolean)
  return raw
}

async function submit(): Promise<void> {
  if (!selected.value) return
  const payload: Record<string, unknown> = {}
  for (const field of fields.value) {
    if (!field.editable) continue
    const raw = formValues.value[field.name]
    if (raw === undefined || raw === '') continue
    payload[field.name] = coerceValue(field, raw)
  }
  statusMessage.value = requiredSignature.value
    ? `Publishing -- step-up authentication (${requiredSignature.value.acrValues.join(', ') || 'no acr configured'}) may be required...`
    : 'Publishing...'
  const result = await composer.publish(selected.value.name, payload, requiredSignature.value ? meaning.value.trim() : undefined)
  statusMessage.value = result.ok
    ? `${result.steppedUp ? 'Stepped up authentication and published' : 'Published'} -- status: ${result.status}, entityId: ${result.entityId}`
    : 'Publish failed -- check connectivity and try again.'
}

void loadEventTypes()
</script>

<template>
  <section aria-label="Event Composer">
    <n-card>
      <h2>Event Composer</h2>
      <p v-if="!loaded">Loading registered event types...</p>
      <template v-else>
        <label>
          Event type
          <!-- ADR-099 -- kept as a native `<select>`, not `n-select`:
               `n-select` renders a teleported custom dropdown rather than
               a native element, which this spec's own `find('select')`/
               `.setValue()` interaction (and jsdom's general lack of
               popover-positioning support) can't drive without a much
               larger test rewrite for no real UX gain over a native
               select for a short, keyboard-native list like this one. -->
          <select @change="(e) => selectEventType(eventTypes[(e.target as HTMLSelectElement).selectedIndex - 1])">
            <option disabled selected value="">-- choose --</option>
            <option v-for="et in eventTypes" :key="`${et.name}:${et.version}`" :value="et.name">{{ et.name }} (v{{ et.version }})</option>
          </select>
        </label>

        <form v-if="selected" data-testid="composer-form" @submit.prevent="submit">
          <div v-for="field in fields" :key="field.name" class="composer-field">
            <template v-if="field.editable">
              <label>
                {{ field.name }}<span v-if="field.required"> *</span>
                <n-checkbox v-if="field.type === 'boolean'" v-model:checked="formValues[field.name] as boolean" />
                <!-- number/integer stays a native input (see the select
                     note above -- n-input has no numeric-typed mode, and
                     an native <input type="number"> already gives correct
                     numeric keyboard/validation behavior for free). -->
                <input v-else-if="field.type === 'number' || field.type === 'integer'" type="number" v-model="formValues[field.name] as string" />
                <n-input v-else v-model:value="formValues[field.name] as string" />
              </label>
            </template>
            <template v-else>
              <p class="composer-field-disabled">{{ field.name }} (masked or nested field -- publish only, not editable here)</p>
            </template>
          </div>
          <div v-if="requiredSignature" class="composer-field" data-testid="composer-signature-block">
            <label>
              Reason for sign-off (Meaning) *
              <n-input v-model:value="meaning" :input-props="({ 'data-testid': 'composer-meaning-input' } as any)" />
            </label>
            <p class="composer-field-hint">
              This event type requires digital sign-off (ADR-066) -- acr: {{ requiredSignature.acrValues.join(', ') || 'any' }}<span v-if="requiredSignature.maxAge"> within {{ requiredSignature.maxAge }}s of authentication</span>.
              You may be stepped up to a stronger authentication context automatically before this publishes.
            </p>
          </div>
          <n-button attr-type="submit" type="primary" :disabled="!canPublish">Publish {{ selected.name }}</n-button>
        </form>
      </template>
      <p v-if="statusMessage" data-testid="composer-status">{{ statusMessage }}</p>
    </n-card>
  </section>
</template>
