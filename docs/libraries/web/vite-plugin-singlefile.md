[← Libraries index](../README.md)

# vite-plugin-singlefile (web)

**What it's for:** a Vite build plugin ([richardtallent/vite-plugin-
singlefile](https://github.com/richardtallent/vite-plugin-singlefile))
that inlines every JS/CSS asset directly into the output `index.html` —
the entire app becomes one static file, openable by double-click in any
browser, no server, no install, no network.

**Why bought, not built:** correctly inlining a real app's build output
(module loading order, asset references, source maps) into one
self-contained file is a solved problem with real edge cases — not
worth reimplementing for what's a small, well-maintained, purpose-built
plugin.

## General usage

```typescript
// vite.config.ts (a second, separate build config from the main app)
import { viteSingleFile } from "vite-plugin-singlefile"

export default defineConfig({
  plugins: [viteSingleFile({ removeViteModuleLoader: true })],
  build: { outDir: "dist-offline-player" }
})
```

## Where this project uses it

`ADR-068` — builds the self-contained offline litigation-review player
as a second build target of the same Vue playback component `ADR-039`'s
live client uses, rather than a second technology stack. The exported
bundle's event data and chain-of-custody verification logic are embedded
directly in the single output file.

## Links

- [github.com/richardtallent/vite-plugin-singlefile](https://github.com/richardtallent/vite-plugin-singlefile)
- [npmjs.com/package/vite-plugin-singlefile](https://www.npmjs.com/package/vite-plugin-singlefile)
