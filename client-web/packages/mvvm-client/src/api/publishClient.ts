import type { ClientOutboxEntry } from '../types'
import { createDpopProof } from './dpop'

export interface PublishResult {
  ok: boolean
  status?: string
  entityId?: string
  conflictFlag?: boolean
  // ADR-066/RFC 9470 -- PublishEndpoints.cs's BuildStepUpChallenge response
  // (401, error="insufficient_user_authentication"), surfaced here as a
  // distinguishable outcome rather than collapsed into the bare `ok: false`
  // every other rejection reason already gets, since a caller (useEventComposer)
  // needs the acr_values/max_age to actually retry with a stepped-up token.
  stepUpRequired?: { acrValues: string[]; maxAge: number | null }
  // A 400 (validation failure) or 403 (Forbidden -- a RequiredClaims gate
  // this caller's own token doesn't satisfy) will never succeed by merely
  // retrying the exact same queued command -- distinct from every other
  // `ok: false` reason (a dropped connection, a real 5xx), which genuinely
  // IS worth retrying once connectivity/the server recovers.
  // `useOutboxStore.flush` uses this to mark such an entry `Failed`
  // (terminal) instead of retrying it forever with no visible signal
  // anything is even wrong -- a real, previously-undiscovered gap found
  // while fixing App.vue's "Dispatch a command" demo panel (TODO.md):
  // `OutboxEntryStatus` already declared a `'Failed'` state (and
  // `exportOutboxBundle`'s own comment already assumed it existed), but
  // nothing anywhere had ever actually set it.
  permanentFailure?: boolean
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
      meaning: entry.meaning,
    }),
  })
  if (!response.ok) {
    if (response.status === 401) {
      // PublishEndpoints.cs's BuildStepUpChallenge returns an RFC 7807
      // ProblemDetails body (type/title/status) -- "title" carries
      // "insufficient_user_authentication", there is no "error" field in
      // the JSON body at all (that string only appears inside the
      // WWW-Authenticate header's own error="..." parameter). Found only
      // by actually driving a real RequiredSignature-gated publish
      // through this client against a live server: every step-up retry
      // before this fix silently fell through to the generic `{ ok:
      // false }` branch below, since `body?.error` was always undefined.
      const body = (await response.json().catch(() => null)) as { title?: string; acrValues?: string[]; maxAge?: number | null } | null
      if (body?.title === 'insufficient_user_authentication') {
        return { ok: false, stepUpRequired: { acrValues: body.acrValues ?? [], maxAge: body.maxAge ?? null } }
      }
    }
    return { ok: false, permanentFailure: response.status === 400 || response.status === 403 }
  }
  const body = (await response.json()) as { status: string; entityId: string; conflictFlag: boolean }
  return { ok: true, status: body.status, entityId: body.entityId, conflictFlag: body.conflictFlag }
}
