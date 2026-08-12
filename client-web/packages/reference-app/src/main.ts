import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'

createApp(App).use(createPinia()).mount('#app')

// ADR-039's Progressive Web App commitment -- app-shell caching + the
// Background Sync outbox-flush trigger (ADR-069's "opportunistic"
// category). Feature-detected, not assumed: Safari/WebKit doesn't support
// Background Sync at all, and this repo's own useOnlineStatus composable
// already covers the "app is open" case identically either way -- the
// Service Worker only adds the "app isn't open when connectivity returns"
// case, per pwa-offline-outbox.md's own documented fallback.
if ('serviceWorker' in navigator) {
  void navigator.serviceWorker.register('/sw.js').then(async (registration) => {
    // ADR-069's "scheduled ('phone home')" trigger category -- Web
    // Periodic Background Sync API, checked rather than assumed:
    // Chromium-only, experimental, requires the `periodic-background-sync`
    // permission (typically only granted once the PWA is installed), zero
    // support in Firefox/Safari as of this writing. Silently skipped, not
    // thrown, everywhere it's unavailable -- the opportunistic (now-armed,
    // see stores/outbox.ts) and explicit/manual triggers already cover
    // every client regardless of this API's own support.
    if (!('periodicSync' in registration)) return
    try {
      const status = await navigator.permissions.query({ name: 'periodic-background-sync' as PermissionName })
      if (status.state !== 'granted') return
      await (registration as ServiceWorkerRegistration & { periodicSync: { register(tag: string, options: { minInterval: number }): Promise<void> } })
        .periodicSync.register('flush-outbox-periodic', { minInterval: 12 * 60 * 60 * 1000 }) // 12h -- the browser treats this as a floor, not a guarantee
    } catch {
      // permissions.query throwing on an unrecognized name (e.g. no
      // browser support for the permission itself) is exactly the same
      // "unavailable, fall back to the other two categories" case as the
      // 'periodicSync in registration' check above -- never fatal.
    }
  })
}
