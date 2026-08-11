// ADR-037/012 -- GraphQL travels over the HTTP QUERY method, never GET/POST
// (PII-safety: a request body, never a URL/access-log-visible query
// string). `fetch` supports an arbitrary method string directly, so no
// special client library is needed for the query half.
const QUERY_METHOD = 'QUERY'

export async function graphqlQuery<T>(hostBaseUrl: string, token: string, query: string, variables?: Record<string, unknown>): Promise<T> {
  const response = await fetch(`${hostBaseUrl}/graphql`, {
    method: QUERY_METHOD,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
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
      const response = await fetch(`${hostBaseUrl}/graphql`, {
        method: QUERY_METHOD,
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ query }),
        signal: controller.signal,
      })
      if (!response.body) throw new Error('Subscription response carried no body stream.')

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
