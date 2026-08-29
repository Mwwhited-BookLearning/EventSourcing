// @eventstore/mvvm-client's own public surface (ADR-062) -- every module
// the reference app (or any other future consumer) needs, re-exported
// from one place rather than deep-importing this package's own internal
// file layout. `deviceInput/types.ts` is re-exported under its own
// namespace object (not `export *`) since it and the root `types.ts`
// both declare types with no natural collision-free flattening -- kept
// explicit rather than risking a silent name clash as more exports are
// added later.
export * from './api/authClient'
export * from './api/dpop'
export * from './api/graphqlClient'
export * from './api/localeClient'
export * from './api/playbackClient'
export * from './api/publishClient'
export * from './api/rbacClient'
export * from './api/streamingClient'
export * from './api/subscriptionBuilder'
export * from './api/ucan'

export * from './composables/useEntityViewActions'
export * from './composables/useEventComposer'
export * from './composables/useLineageExportAndPlayback'
export * from './composables/useOnlineStatus'
export * from './composables/usePendingAuthorityQueue'
export * from './composables/useRelyingPartyAccess'

export * from './db/indexedDb'

export * from './deviceInput/NativeBridgeInputSource'
export * from './deviceInput/RecordingAgent'
export * from './deviceInput/WebBluetoothInputSource'
export * from './deviceInput/WebHidInputSource'
export * from './deviceInput/WebSerialInputSource'
export * from './deviceInput/WebUsbInputSource'
export * from './deviceInput/deviceReadingOutbox'
export * as deviceInputTypes from './deviceInput/types'

export * from './i18n/locale'
export * from './i18n/translations'

// Named, aliased re-export (not `export *`) -- both this module and
// playback/bundle.ts export a `parseNdjson`/`toNdjson` pair with
// different signatures (one parses an OutboxBundle, the other a
// LineageExportBundle); ES module `export *` silently DROPS an
// ambiguously-named binding from the resulting namespace rather than
// merging or erroring, so a blanket `export *` here would have quietly
// made whichever one won unavailable (found only by running this: an
// OfflineBundleViewer test failed with "bundle.events is not iterable"
// because it received outbox's own OutboxBundle shape instead of
// playback's LineageExportBundle one).
export { parseNdjson as parseOutboxBundleNdjson, toNdjson as toOutboxBundleNdjson, type OutboxBundle, type OutboxBundleManifest, type OutboxBundleEntry } from './outbox/bundle'
export * from './outbox/exportImport'

export * from './playback/bundle'
export * from './playback/verifyBundle'

export * from './stores/entityCache'
export * from './stores/outbox'
export * from './stores/viewDefinitions'

export * from './theme/tokens'

export * from './types'
