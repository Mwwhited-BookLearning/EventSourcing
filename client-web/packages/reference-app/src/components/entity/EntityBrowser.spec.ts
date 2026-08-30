import { describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import EntityBrowser from './EntityBrowser.vue'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}

// Same DPoP-proof-is-a-macrotask characteristic EventComposer.spec.ts's own
// comment documents -- flushPromises() alone doesn't drain it.
async function flushAll(): Promise<void> {
  await flushPromises()
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 5))
}

const props = {
  instanceId: 'instance-a', hostBaseUrl: 'https://host', authBaseUrl: 'https://auth',
  appId: 'mvvm-demo', entityType: 'orderplaced', clientId: 'follower-client', clientSecret: 'follower-client-secret', scope: 'events:follow',
}

function bodyOf(call: unknown[]): { query: string } {
  return JSON.parse((call[1] as RequestInit).body as string)
}

// Routes each mocked fetch by the REQUEST'S OWN content, not call order --
// fetchPage's list and count queries fire concurrently (Promise.all), so
// which of the two actually reaches `fetch()` first is not guaranteed
// (each independently awaits its own DPoP proof macrotask first). Only
// the token request and the introspection query are genuinely sequential
// (each explicitly awaited before the next step starts).
function installEntityBrowserFetchMock(rows: unknown[], totalCount: number, fields: Array<{ name: string; type: { kind: string; name: string | null } }> = []): void {
  global.fetch = vi.fn(async (url: unknown, init?: RequestInit) => {
    if (String(url).includes('/connect/token')) return jsonResponse({ access_token: 'browser-token' })
    const { query } = JSON.parse(init!.body as string) as { query: string }
    if (query.includes('__type')) return jsonResponse({ data: { __type: { fields } } })
    if (query.startsWith('query { entityCount_')) return jsonResponse({ data: { entityCount_mvvm_demo_orderplaced: totalCount } })
    return jsonResponse({ data: { entities_mvvm_demo_orderplaced: rows } })
  }) as unknown as typeof fetch
}

// TODO.md, "Data grids: a real paged server query" -- EntityBrowser now
// fetches real server pages (useEntityBrowserQuery) instead of listing
// whatever useEntityCacheStore's own REPLAY subscription had already
// accumulated, so this spec mocks the underlying fetch calls directly
// (token, introspection, list, count), the same convention
// EventComposer.spec.ts's own comment already established for a
// composable-driven component, rather than pre-seeding a Pinia store.
describe('EntityBrowser', () => {
  it('shows a message when the server page has no matching entities', async () => {
    installEntityBrowserFetchMock([], 0)

    const wrapper = mount(EntityBrowser, { props })
    await flushAll()
    expect(wrapper.text()).toContain('No matching entities')
  })

  it('lists a fetched page of entities and emits select on View', async () => {
    installEntityBrowserFetchMock(
      [
        { entityId: 'mvvm-demo:orderplaced:o-1', authorityStatus: 'accepted', orderId: 'o-1', amount: 150 },
        { entityId: 'mvvm-demo:orderplaced:o-2', authorityStatus: 'accepted', orderId: 'o-2', amount: 200 },
      ],
      2,
      [{ name: 'orderId', type: { kind: 'SCALAR', name: 'String' } }, { name: 'amount', type: { kind: 'SCALAR', name: 'Float' } }],
    )

    const wrapper = mount(EntityBrowser, { props })
    await flushAll()
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)

    await wrapper.findAll('button').find((b) => b.text() === 'View')!.trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual(['mvvm-demo:orderplaced:o-1'])
  })

  it('fetches a fresh page with the contains argument when the filter box changes', async () => {
    installEntityBrowserFetchMock([], 0)

    const wrapper = mount(EntityBrowser, { props })
    await flushAll()

    installEntityBrowserFetchMock([{ entityId: 'mvvm-demo:orderplaced:o-9', authorityStatus: 'accepted' }], 1)
    await wrapper.find('[data-testid="entity-browser-filter"]').setValue('o-9')
    await new Promise((resolve) => setTimeout(resolve, 350)) // past the 300ms debounce
    await flushAll()

    const listCall = vi.mocked(global.fetch).mock.calls.find((call) => bodyOf(call).query.startsWith('query { entities_'))!
    expect(bodyOf(listCall).query).toContain('contains: "o-9"')
    expect(wrapper.findAll('tbody tr')).toHaveLength(1)
  })
})
