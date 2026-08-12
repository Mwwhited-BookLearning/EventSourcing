import { createDpopProof } from './dpop'

// ADR-037/012 -- GraphQL travels over the HTTP QUERY method, never GET/POST
// (PII-safety: a request body, never a URL/access-log-visible query
// string). `fetch` supports an arbitrary method string directly, so no
// special client library is needed for the query half.
const QUERY_METHOD = 'QUERY'

export async function graphqlQuery<T>(hostBaseUrl: string, token: string, query: string, variables?: Record<string, unknown>): Promise<T> {
  const url = `${hostBaseUrl}/graphql`
  const response = await fetch(url, {
    method: QUERY_METHOD,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      DPoP: await createDpopProof(QUERY_METHOD, url, token), // ADR-017 -- DpopValidationMiddleware gates every eventstore endpoint, not just token issuance
    },
    body: JSON.stringify({ query, variables }),
  })
  const body = (await response.json()) as { data?: T; errors?: Array<{ message: string }> }
  if (body.errors && body.errors.length > 0) throw new Error(body.errors.map((e) => e.message).join('; '))
  return body.data as T
}

// A GraphQL Subscription streamed as SSE (ADR-037's own transport,
// FollowSubscriptionTypeModule/GraphQlEndpoints server-side) -- `EventSource`
// only ever issues GET, so this hand-rolls the same fetch+ReadableStream
// line-parsing this repo's own C# HTTP tests already use
// (GraphQlHttpSqliteTests.SubscribingOverRealHttpStreamsAMatchingEventAsSse),
// now on the actual client side of that same wire protocol. Returns an
// unsubscribe function; the caller is responsible for calling it to abort
// the underlying stream (e.g. on component unmount).
export function graphqlSubscribe<T>(
  hostBaseUrl: string,
  token: string,
  query: string,
  onMessage: (data: T) => void,
  onError?: (error: unknown) => void,
): () => void {
  const controller = new AbortController()

  void (async () => {
    try {
      const url = `${hostBaseUrl}/graphql`
      const response = await fetch(url, {
        method: QUERY_METHOD,
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
          DPoP: await createDpopProof(QUERY_METHOD, url, token), // ADR-017 -- same gate as graphqlQuery above
        },
        body: JSON.stringify({ query }),
        signal: controller.signal,
      })
      if (!response.body) throw new Error('Subscription response carried no body stream.')

      // A rejection at connect time (e.g. the caller's token lacks the
      // required scope) never enters SSE framing at all -- HotChocolate
      // returns a single plain JSON body ({"errors":[...]}), not a
      // "data: "-prefixed line. Found while verifying this exact path
      // directly (a real GraphQL Forbidden response silently produced no
      // onMessage AND no onError -- the "Waiting for the first event"
      // symptom would have shown no diagnostic trace at all for a genuine
      // rejection, not just for a stream with nothing to deliver yet).
      const isSse = (response.headers.get('content-type') ?? '').includes('text/event-stream')
      if (!isSse) {
        const body = (await response.json()) as { errors?: Array<{ message: string }> }
        if (body.errors && body.errors.length > 0) onError?.(new Error(body.errors.map((e) => e.message).join('; ')))
        return
      }

      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })

        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const frame = JSON.parse(line.slice('data: '.length)) as { data?: T; errors?: Array<{ message: string }> }
          if (frame.errors && frame.errors.length > 0) {
            onError?.(new Error(frame.errors.map((e) => e.message).join('; ')))
            continue
          }
          if (frame.data) onMessage(frame.data)
        }
      }
    } catch (error) {
      if (!controller.signal.aborted) onError?.(error)
    }
  })()

  return () => controller.abort()
}
