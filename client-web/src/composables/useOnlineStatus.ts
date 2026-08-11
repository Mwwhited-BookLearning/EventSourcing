import { onMounted, onUnmounted, ref } from 'vue'

// ADR-069's "opportunistic" outbox-flush trigger category, web-target
// realization: `window`'s `online` event is this app's own equivalent of
// Background Sync firing on reconnect, for the common case where the app
// itself is already open when connectivity returns (Background Sync/
// Service Worker covers the "app isn't open" case separately, see
// public/sw.js). `onOnline` is deliberately just a callback, not a direct
// outbox reference -- this composable stays framework/outbox-agnostic per
// docs/patterns/mvvm-client-architecture.md's own composable guardrails.
export function useOnlineStatus(onOnline: () => void) {
  const isOnline = ref(typeof navigator === 'undefined' ? true : navigator.onLine)

  function handleOnline() {
    isOnline.value = true
    onOnline()
  }
  function handleOffline() {
    isOnline.value = false
  }

  onMounted(() => {
    window.addEventListener('online', handleOnline)
    window.addEventListener('offline', handleOffline)
  })
  onUnmounted(() => {
    window.removeEventListener('online', handleOnline)
    window.removeEventListener('offline', handleOffline)
  })

  return { isOnline }
}
