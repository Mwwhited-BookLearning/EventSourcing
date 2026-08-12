<script setup lang="ts">
// ADR-024 (ConflictFlag), ADR-029 (LateArrivalFlag), ADR-035 (AuthorityStatus)
// -- one shared generic "flag" convention, per ADR-039's own explicit
// requirement, never three bespoke indicators. Both GenericFallbackView and
// a ViewDefinition template's own default flag slot (TemplateRenderer) use
// this exact same component -- there is only one implementation of this
// convention in the whole client.
defineProps<{
  conflictFlag: boolean
  lateArrivalFlag: boolean
  authorityStatus: string
}>()
</script>

<template>
  <div class="flag-row" role="status">
    <span class="flag" :class="{ 'flag--active': conflictFlag }" data-testid="conflict-flag">
      {{ conflictFlag ? '⚠ ConflictFlag' : 'ConflictFlag: false' }}
    </span>
    <span class="flag" :class="{ 'flag--active': lateArrivalFlag }" data-testid="late-arrival-flag">
      {{ lateArrivalFlag ? '⚠ LateArrivalFlag' : 'LateArrivalFlag: false' }}
    </span>
    <span class="flag" data-testid="authority-status"> AuthorityStatus: {{ authorityStatus }} </span>
  </div>
</template>

<style scoped>
.flag-row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}
.flag {
  padding: 0.15rem 0.5rem;
  border-radius: 0.25rem;
  border: 1px solid var(--duplex-border, #ccc);
}
.flag--active {
  border-color: var(--duplex-flag-active, #b45309);
  font-weight: 600;
}
</style>
