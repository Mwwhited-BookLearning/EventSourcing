import { createApp } from 'vue'
import OfflineBundleViewer from '../src/components/playback/OfflineBundleViewer.vue'

// The vite-plugin-singlefile build target inlines this bundle's data
// directly into the built index.html as a script tag (see build.ts's own
// "embed and rebuild" step) -- `window.__DUPLEX_LINEAGE_EXPORT_BUNDLE__` is
// set BEFORE this module runs, by that inlined `<script>`, never fetched
// over the network (ADR-068 §3: "zero external requests, opens by
// double-click"). This is the alternate build target of the SAME
// OfflineBundleViewer.vue the connected app can also mount from a file
// picker -- one component, two entry points, not a second implementation.
declare global {
  interface Window {
    __DUPLEX_LINEAGE_EXPORT_BUNDLE__?: string
  }
}

const bundleNdjson = window.__DUPLEX_LINEAGE_EXPORT_BUNDLE__ ?? ''

createApp(OfflineBundleViewer, { bundleNdjson }).mount('#app')
