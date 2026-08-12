import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { graphqlSubscribe } from './graphqlClient'

function sseResponse(frames: string[]): Response {
  const body = frames.map((f) => `data: ${f}\n\n`).join('')
  return new Response(body, { status: 200, headers: { 'content-type': 'text/event-stream' } })
}

function jsonResponse(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/graphql-response+json' } })
}

describe('graphqlSubscribe', () => {
  const originalFetch = global.fetch

  beforeEach(() => {
    vi.stubGlobal('crypto', { ...globalThis.crypto, subtle: globalThis.crypto.subtle, randomUUID: () => 'test-uuid' })
  })
  afterEach(() => {
    global.fetch = originalFetch
    vi.unstubAllGlobals()
  })

  it('delivers a real SSE-framed message to onMessage', async () => {
    global.fetch = vi.fn().mockResolvedValue(sseResponse(['{"data":{"foo":"bar"}}']))
    const onMessage = vi.fn()
    const onError = vi.fn()
    graphqlSubscribe('https://host', 'token', 'subscription { foo }', onMessage, onError)
    await new Promise((r) => setTimeout(r, 50))
    expect(onMessage).toHaveBeenCalledWith({ foo: 'bar' })
    expect(onError).not.toHaveBeenCalled()
  })

  // Found by actually driving this against a real rejection (a token
  // lacking the required scope) -- a connect-time GraphQL error never
  // enters SSE framing, arriving instead as one plain JSON body with no
  // "data: " prefix at all. Before this fix, the parsing loop only ever
  // recognized that prefix, so this response produced neither onMessage
  // nor onError -- a genuine rejection looked identical to "nothing to
  // deliver yet."
  it('surfaces a plain JSON error response (rejected before SSE framing starts) via onError, not silence', async () => {
    global.fetch = vi.fn().mockResolvedValue(jsonResponse({ errors: [{ message: "Forbidden -- caller's token does not hold the required scope." }] }))
    const onMessage = vi.fn()
    const onError = vi.fn()
    graphqlSubscribe('https://host', 'token', 'subscription { foo }', onMessage, onError)
    await new Promise((r) => setTimeout(r, 50))
    expect(onMessage).not.toHaveBeenCalled()
    expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining('Forbidden') }))
  })
})
