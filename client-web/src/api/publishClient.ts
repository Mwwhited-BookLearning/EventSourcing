import type { ClientOutboxEntry } from '../types'
import { createDpopProof } from './dpop'

export interface PublishResult {
  ok: boolean
  status?: string
  entityId?: string
  conflictFlag?: boolean
}

// The "round trip through ADR-021's Entity Store" ADR-039 requires -- the
// ordinary, already-real REST /publish/{eventType} endpoint (ADR-023), not
// a new server surface. `commandId` is passed as the request's own
// `eventId` -- ADR-011's Idempotent Receiver dedups on it, which is the
// entire reason redelivering the same queued command after a reconnect
// never applies it twice; no second dedup mechanism exists on the client.
export async function publishCommand(hostBaseUrl: string, token: string, entry: ClientOutboxEntry): Promise<PublishResult> {
  const url = `${hostBaseUrl}/publish/${entry.eventType}`
  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      DPoP: await createDpopProof('POST', url, token), // ADR-017
    },
    body: JSON.stringify({
      appId: entry.appId,
      eventId: entry.commandId,
      schemaVersion: entry.schemaVersion,
      expectedVersion: entry.expectedVersion,
      payload: entry.patch,
      // Absent/false on every pre-existing entry and ordinary command --
      // PublishService's own existing AuthorityStatus logic already
      // treats a request with none of these three set as the ordinary
      // "accepted" default, unchanged by this addition (ADR-070).
      reviewPending: entry.reviewPending,
      attestedActorId: entry.attestedActorId,
      attestedClaims: entry.attestedClaims,
    }),
  })
  if (!response.ok) return { ok: false }
  const body = (await response.json()) as { status: string; entityId: string; conflictFlag: boolean }
  return { ok: true, status: body.status, entityId: body.entityId, conflictFlag: body.conflictFlag }
}
