import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { viteSingleFile } from 'vite-plugin-singlefile'

// ADR-068 §3 / docs/libraries/web/vite-plugin-singlefile.md -- a SECOND,
// separate build config from the main app's vite.config.ts, sharing the
// exact same Vue component (OfflineBundleViewer.vue) via offline-player/
// main.ts. Output is one self-contained index.html: every JS/CSS asset
// inlined, zero external requests, openable by double-click.
export default defineConfig({
  root: 'offline-player',
  plugins: [vue(), viteSingleFile({ removeViteModuleLoader: true })],
  build: {
    outDir: '../dist-offline-player',
    emptyOutDir: true,
  },
})
