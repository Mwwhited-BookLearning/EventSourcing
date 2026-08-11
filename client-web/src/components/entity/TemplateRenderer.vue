<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ClientEntityCacheEntry } from '../../types'
import FlagRow from './FlagRow.vue'
import { isRtlLocale } from '../../i18n/locale'

// ADR-039's "small injected binding runtime" -- raw HTML+JS
// (`templateContent`), never precompiled, interpreted by this generic
// renderer at runtime. Deliberately minimal -- one-way `{{ field }}` data
// interpolation, `{{ t:key }}` translation-key resolution and
// `{{ field:date }}`/`{{ field:number }}` locale-aware formatting
// (ADR-087), plus a `data-command-field`/`data-command-value-from`
// attribute convention for dispatching a command -- not a full templating
// engine, exactly what the ADR's own Consequences call for ("needing zero
// extra machinery" beyond what the web platform gives for free). The
// flag-rendering convention below is the SAME FlagRow component
// GenericFallbackView uses -- one implementation, not a template-specific
// second one.
const props = defineProps<{
  templateContent: string
  entry: ClientEntityCacheEntry
  // ADR-087 -- both default to the server's own DefaultRequestCulture
  // (HostCoreExtensions.cs, "en-US") so every pre-item-46 caller of this
  // component is unaffected; a caller resolving a real negotiated locale
  // (client-web/src/i18n/locale.ts's resolveLocale, read from a response's
  // own Content-Language header) passes it explicitly instead.
  locale?: string
  translations?: Record<string, string>
}>()

const emit = defineEmits<{ command: [fieldPath: string, value: string] }>()

const container = ref<HTMLDivElement | null>(null)

// {{ t:key }} (translation) or {{ field }}/{{ field:date }}/{{ field:number }}
// (data binding, optionally locale-formatted) -- one regex, matching
// TranslationKeyValidator.cs's own server-side pattern exactly, since a
// template registered as compliant there must be interpreted identically
// here (the same two legitimate non-literal shapes on both sides).
const INTERPOLATION = /\{\{\s*(?:(t):([\w.]+)|([\w.]+)(?::(date|number))?)\s*\}\}/g

// A masked field (x-masking, ADR-009) resolves over the live subscription
// path (subscriptionBuilder.ts's own masked-aware selection) to the same
// three-way { value, masked, erased } wrapper server-side masking already
// produces everywhere else (revealField, the offline bundle) -- exactly
// one of value/masked is ever populated per MaskedFieldTypes.cs's own
// comment, erased is a separate, later-arriving flag. Never a raw scalar
// once a field is masked, so this must be checked before falling through
// to String(value) below (which would otherwise render "[object Object]").
function isMaskedWrapper(value: unknown): value is { value: unknown; masked: string | null; erased: boolean | null } {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && ('value' in value || 'masked' in value || 'erased' in value)
}

function interpolate(template: string, entry: ClientEntityCacheEntry, locale: string, translations: Record<string, string>): string {
  return template.replace(INTERPOLATION, (_match, translationMarker: string | undefined, translationKey: string | undefined, fieldPath: string | undefined, format: string | undefined) => {
    if (translationMarker === 't') return translations[translationKey!] ?? `[${translationKey}]` // an unresolved key is surfaced visibly, never silently blanked -- easier to catch a missing placeholder resource during development
    const path = fieldPath!
    if (path === 'entityId') return entry.entityId
    if (path === 'entityType') return entry.entityType
    let value: unknown = entry.data[path]
    if (isMaskedWrapper(value)) {
      if (value.erased) return translations['field_erased'] ?? '(erased)'
      value = value.value ?? value.masked ?? ''
    }
    if (value === undefined || value === null) return ''
    if (format === 'number' && typeof value === 'number') return new Intl.NumberFormat(locale).format(value)
    if (format === 'date') return new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(String(value)))
    return String(value)
  })
}

function render(): void {
  if (!container.value) return
  const locale = props.locale ?? 'en-US'
  container.value.innerHTML = interpolate(props.templateContent, props.entry, locale, props.translations ?? {})
  // ADR-087's RTL requirement -- `dir` (a real HTML global attribute, not
  // a CSS Logical Properties substitute) is what actually flips a
  // browser's own bidi algorithm and logical-property resolution
  // (`margin-inline-start` etc. in this template's own CSS, per that
  // ADR's convention) for the WHOLE rendered subtree in one place, rather
  // than requiring every template author to set it themselves.
  container.value.setAttribute('dir', isRtlLocale(locale) ? 'rtl' : 'ltr')
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
watch(() => [props.templateContent, props.entry, props.locale, props.translations], render, { deep: true })
</script>

<template>
  <section aria-label="Entity (ViewDefinition-rendered)">
    <div ref="container" data-testid="template-container"></div>
    <FlagRow :conflict-flag="entry.conflictFlag" :late-arrival-flag="entry.lateArrivalFlag" :authority-status="entry.authorityStatus" />
  </section>
</template>
