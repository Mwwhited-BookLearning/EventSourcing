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
  void navigator.serviceWorker.register('/sw.js')
}
