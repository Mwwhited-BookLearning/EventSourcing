import { inject, type InjectionKey, type Ref } from 'vue'
import type { ClientConfig, useEntityViewActions } from '@eventstore/mvvm-client'

// App.vue owns every piece of business state and logic this shell has
// always owned (ADR-039's per-instance subscription, the outbox, the
// amount-dispatch command) -- ADR-099 only changes how that state reaches
// each tab: `provide`/`inject` instead of template refs passed down
// through a single big `<template v-if>` tree, since each tab is now a
// route-level view component instead of a template branch.
export interface AppState {
  config: ClientConfig
  viewActions: ReturnType<typeof useEntityViewActions>
  currentEntityId: Ref<string>
  amountInput: Ref<string>
  statusMessage: Ref<string>
  submitAmountCommand: () => Promise<void>
  selectFromBrowser: (entityId: string) => void
}

export const APP_STATE_KEY: InjectionKey<AppState> = Symbol('duplex-app-state')

export function useAppState(): AppState {
  const state = inject(APP_STATE_KEY)
  if (!state) throw new Error('useAppState() called outside App.vue\'s provider -- every route view must render under <router-view> inside App.vue')
  return state
}
