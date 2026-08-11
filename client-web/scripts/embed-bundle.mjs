#!/usr/bin/env node
// ADR-068 §4 -- "the matching player travels with the export": takes the
// already-built, generic dist-offline-player/index.html (npm run
// build:offline-player) and a downloaded lineage export bundle's NDJSON,
// and produces one self-contained HTML file with that specific bundle's
// data embedded. Run once per export, never baked into the reusable
// build itself (the build target has no real bundle to embed at build
// time -- see offline-player/index.html's own placeholder comment).
import { readFileSync, writeFileSync } from 'node:fs'

const [, , distIndexPath, bundleNdjsonPath, outputPath] = process.argv
if (!distIndexPath || !bundleNdjsonPath || !outputPath) {
  console.error('Usage: node scripts/embed-bundle.mjs <dist-offline-player/index.html> <bundle.ndjson> <output.html>')
  process.exit(1)
}

const distHtml = readFileSync(distIndexPath, 'utf8')
const bundleNdjson = readFileSync(bundleNdjsonPath, 'utf8')

// JSON.stringify produces a JS-string-literal-safe escaped form (quotes,
// backslashes, newlines all handled) -- the placeholder itself sits inside
// double quotes in offline-player/index.html, so the surrounding quotes
// from JSON.stringify are stripped before substitution.
const escaped = JSON.stringify(bundleNdjson).slice(1, -1)
if (!distHtml.includes('DUPLEX_BUNDLE_PLACEHOLDER')) {
  console.error(`${distIndexPath} does not contain the expected DUPLEX_BUNDLE_PLACEHOLDER marker -- was it built from offline-player/index.html?`)
  process.exit(1)
}

writeFileSync(outputPath, distHtml.replace('DUPLEX_BUNDLE_PLACEHOLDER', escaped))
console.log(`Wrote self-contained offline player to ${outputPath}`)
