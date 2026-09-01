import { defineStore } from 'pinia'

// A manual override layered on top of the browser's own REAL connectivity
// signal (`navigator.onLine`) -- this store never replaces that signal,
// only adds a second, independently-settable reason to withhold a flush.
// `navigator.onLine` itself can't be reassigned from page script, so a
// "force offline" demo/test control needs its own piece of state that
// every flush-gating call site consults alongside it.
export const useConnectivityStore = defineStore('connectivity', {
  state: () => ({
    forcedOffline: false,
  }),
  actions: {
    goOffline() {
      this.forcedOffline = true
    },
    goOnline() {
      this.forcedOffline = false
    },
    // A plain method, deliberately NOT a `getters` entry -- Pinia getters
    // are wrapped in Vue's `computed()`, which only re-evaluates when a
    // REACTIVE dependency changes. `navigator.onLine` is a plain,
    // non-reactive global; a cached computed here would capture its first
    // read and never notice a later real connectivity change unless
    // `forcedOffline` also happened to change at the same moment (found by
    // reasoning through Pinia's own getter-caching semantics before
    // shipping this, not discovered after). A plain method re-reads both
    // the forced override and the real navigator value fresh on every
    // call, which is what every flush-gating call site actually needs.
    isEffectivelyOnline(): boolean {
      return !this.forcedOffline && (typeof navigator === 'undefined' || navigator.onLine)
    },
  },
})
