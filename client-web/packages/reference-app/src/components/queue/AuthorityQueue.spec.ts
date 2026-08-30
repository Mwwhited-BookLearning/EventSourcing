import { describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import AuthorityQueue from './AuthorityQueue.vue'

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })
}

// Same macrotask characteristic documented in EventComposer.spec.ts/
// OfflineBundleViewer.spec.ts -- crypto.subtle (DPoP proof generation)
// doesn't resolve within a plain flushPromises() microtask drain.
async function flushAll(): Promise<void> {
  await flushPromises()
  for (let i = 0; i < 5; i++) await new Promise((resolve) => setTimeout(resolve, 5))
}

const props = {
  hostBaseUrl: 'https://host',
  authBaseUrl: 'https://auth',
  appId: 'trial1',
  raiserEventType: 'IonmAlertRaised',
  decisionClientId: 'vitals-pi-client',
  decisionClientSecret: 'vitals-pi-client-secret',
  isPending: (payload: Record<string, unknown>) => payload.authorityStatus === 'pending_review',
  title: 'Principal Investigator Queue',
  reviewerLabel: 'Reviewing as (PI)',
  reviewerDefault: 'pi-1',
}

describe('AuthorityQueue', () => {
  it('shows a pending raiser event and gates Accept/Reject on the Meaning field', async () => {
    let raiserOnMessage: ((data: Record<string, Record<string, unknown>>) => void) | undefined
    global.fetch = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.includes('/connect/token')) return jsonResponse({ access_token: 'follower-token' })
      const body = init?.body ? (JSON.parse(init.body as string) as { query: string }) : { query: '' }
      if (body.query.includes('__type')) {
        const fields = body.query.includes('authoritydecision') ? ['targetEventId'] : ['eventId', 'alertId', 'authorityStatus']
        return jsonResponse({ data: { __type: { fields: fields.map((name) => ({ name })) } } })
      }
      throw new Error(`unexpected fetch: ${url}`)
    })

    // graphqlSubscribe uses its own streaming fetch path -- easier to spy on
    // the module directly here than hand-roll an SSE ReadableStream mock,
    // matching how useEntityViewActions.spec.ts already tests subscribe().
    // Imported by its own underlying module path, not the package's index
    // barrel -- Vitest's ESM-export spying needs to mutate the EXACT same
    // module instance usePendingAuthorityQueue.ts itself imports
    // graphqlSubscribe from; spying through an `export *` re-export
    // barrel's own copy of the binding doesn't reliably propagate back to
    // that original module in every case (found only by running this: the
    // spy silently never intercepted the real call once this test moved
    // to a separate package from the composable it's testing).
    const graphqlClientModule = await import('@eventstore/mvvm-client/src/api/graphqlClient')
    vi.spyOn(graphqlClientModule, 'graphqlSubscribe').mockImplementation((_host, _token, query, onMessage) => {
      if ((query as string).includes('ionmalertraised')) raiserOnMessage = onMessage as typeof raiserOnMessage
      return () => {}
    })

    const wrapper = mount(AuthorityQueue, { props })
    await flushAll()

    raiserOnMessage!({ on_trial1_ionmalertraised: { eventId: 'evt-1', alertId: 'alert-1', authorityStatus: 'pending_review' } })
    await flushAll()

    expect(wrapper.find('[data-testid="queue-list"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('alertId: alert-1')

    const acceptButton = wrapper.findAll('button').find((b) => b.text() === 'Accept')!
    expect((acceptButton.element as HTMLButtonElement).disabled).toBe(true)

    await wrapper.find('[data-testid="queue-meaning-evt-1"]').setValue('reviewed')
    expect((acceptButton.element as HTMLButtonElement).disabled).toBe(false)

    vi.restoreAllMocks()
  })

  // Found via a real playbook screenshot (MeridianKycAnalystQueuePlaybookTests)
  // -- a masked field (x-masking's { value, masked, erased } wrapper) rendered
  // as the literal, useless string "[object Object]" before this fix.
  it('renders a masked field as "[masked/complex]", never "[object Object]"', async () => {
    let raiserOnMessage: ((data: Record<string, Record<string, unknown>>) => void) | undefined
    global.fetch = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.includes('/connect/token')) return jsonResponse({ access_token: 'follower-token' })
      const body = init?.body ? (JSON.parse(init.body as string) as { query: string }) : { query: '' }
      if (body.query.includes('__type')) {
        const fields = body.query.includes('authoritydecision') ? ['targetEventId'] : ['eventId', 'applicantId', 'matchFound', 'matchedName']
        return jsonResponse({ data: { __type: { fields: fields.map((name) => ({ name })) } } })
      }
      throw new Error(`unexpected fetch: ${url}`)
    })

    const graphqlClientModule = await import('@eventstore/mvvm-client/src/api/graphqlClient')
    vi.spyOn(graphqlClientModule, 'graphqlSubscribe').mockImplementation((_host, _token, query, onMessage) => {
      if ((query as string).includes('sanctionsscreeningperformed')) raiserOnMessage = onMessage as typeof raiserOnMessage
      return () => {}
    })

    const wrapper = mount(AuthorityQueue, {
      props: { ...props, raiserEventType: 'SanctionsScreeningPerformed', isPending: (payload: Record<string, unknown>) => payload.matchFound === true },
    })
    await flushAll()

    raiserOnMessage!({
      on_trial1_sanctionsscreeningperformed: {
        eventId: 'evt-2',
        applicantId: 'applicant-1',
        matchFound: true,
        matchedName: { value: null, masked: 'JXXX', erased: null },
      },
    })
    await flushAll()

    expect(wrapper.text()).toContain('matchedName: [masked/complex]')
    expect(wrapper.text()).not.toContain('[object Object]')

    vi.restoreAllMocks()
  })

  // ADR-100 -- a configured chartable field renders as a real gauge, not
  // plain text, and is excluded from the plain-text summary column so the
  // same value isn't shown twice.
  it('renders a configured chartable field as a gauge chart, excluded from the plain-text summary', async () => {
    let raiserOnMessage: ((data: Record<string, Record<string, unknown>>) => void) | undefined
    global.fetch = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.includes('/connect/token')) return jsonResponse({ access_token: 'follower-token' })
      const body = init?.body ? (JSON.parse(init.body as string) as { query: string }) : { query: '' }
      if (body.query.includes('__type')) {
        const fields = body.query.includes('authoritydecision')
          ? ['targetEventId']
          : ['eventId', 'applicantId', 'matchFound', 'matchConfidence']
        return jsonResponse({ data: { __type: { fields: fields.map((name) => ({ name })) } } })
      }
      throw new Error(`unexpected fetch: ${url}`)
    })

    const graphqlClientModule = await import('@eventstore/mvvm-client/src/api/graphqlClient')
    vi.spyOn(graphqlClientModule, 'graphqlSubscribe').mockImplementation((_host, _token, query, onMessage) => {
      if ((query as string).includes('sanctionsscreeningperformed')) raiserOnMessage = onMessage as typeof raiserOnMessage
      return () => {}
    })

    const wrapper = mount(AuthorityQueue, {
      props: {
        ...props,
        raiserEventType: 'SanctionsScreeningPerformed',
        isPending: (payload: Record<string, unknown>) => payload.matchFound === true,
        chartableFields: [{ field: 'matchConfidence', chartType: 'gauge' as const, label: 'Match confidence' }],
      },
    })
    await flushAll()

    raiserOnMessage!({
      on_trial1_sanctionsscreeningperformed: { eventId: 'evt-3', applicantId: 'applicant-3', matchFound: true, matchConfidence: 0.87 },
    })
    await flushAll()
    await new Promise((resolve) => setTimeout(resolve, 20)) // ECharts' own render happens a tick after the DOM update, see GaugeChart.spec.ts's identical note

    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.html()).toContain('87%')
    expect(wrapper.text()).not.toContain('matchConfidence: 0.87')

    vi.restoreAllMocks()
  })
})
