import * as clientDb from './indexedDb'

// TODO.md's resume-cursor gap -- persists the last SequenceNumber this
// instance has durably applied for a given (instanceId, appId, eventType)
// subscription target, so a reconnect (a page reload, a dropped connection,
// a stopped/restarted instance) can resume with `mode: REPLAY,
// fromSequenceNumber: <cursor>` instead of either a blind `TAIL` (which
// silently misses anything published while disconnected) or an unconditional
// `REPLAY` from 0 (which re-downloads and re-applies the instance's entire
// history on every reconnect). Not a Pinia store -- nothing here is rendered
// reactively, it's read once per subscribe() call and written once per
// delivered event, the same non-reactive persistence shape the outbox/entity
// cache stores wrap around IndexedDB but without the reactive state layer on
// top, since there's no UI surface that needs to observe a cursor value
// changing.
export interface SubscriptionCursorEntry {
  id: string
  sequenceNumber: number
}

function keyFor(instanceId: string, appId: string, eventType: string): string {
  return `${instanceId}:${appId}:${eventType}`
}

// EventTailReader.TailAsync's own predicate is `SequenceNumber > lastSeen`
// (server-side, EventStore.Follow.Api) -- the last-seen value itself, not
// `+ 1`, is the correct `fromSequenceNumber` to reconnect with. 0 (never a
// real SequenceNumber -- the server's own log starts at 1) is the correct
// default for an instance that has never durably applied anything yet.
export async function getCursor(instanceId: string, appId: string, eventType: string): Promise<number> {
  const entry = await clientDb.get<SubscriptionCursorEntry>(clientDb.SUBSCRIPTION_CURSOR_STORE, keyFor(instanceId, appId, eventType))
  return entry?.sequenceNumber ?? 0
}

export async function setCursor(instanceId: string, appId: string, eventType: string, sequenceNumber: number): Promise<void> {
  const entry: SubscriptionCursorEntry = { id: keyFor(instanceId, appId, eventType), sequenceNumber }
  await clientDb.put(clientDb.SUBSCRIPTION_CURSOR_STORE, entry)
}
