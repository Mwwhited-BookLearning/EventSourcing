import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import GenericFallbackView from './GenericFallbackView.vue'
import FlagRow from './FlagRow.vue'
import type { ClientEntityCacheEntry } from '../../types'

function makeEntry(overrides: Partial<ClientEntityCacheEntry> = {}): ClientEntityCacheEntry {
  return {
    entityId: 'mvvm-demo:shipment:s-1',
    instanceId: 'instance-b',
    entityType: 'shipment',
    data: { carrier: 'UPS' },
    extensions: {},
    schemaVersion: 1,
    conflictFlag: false,
    lateArrivalFlag: false,
    authorityStatus: 'accepted',
    cachedAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('GenericFallbackView (ADR-039\'s required fallback -- never a blank/failed render)', () => {
  it('renders an entity with no registered ViewDefinition via the generic property-list view', () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry() } })
    expect(wrapper.text()).toContain('shipment')
    expect(wrapper.text()).toContain('carrier')
    expect(wrapper.text()).toContain('UPS')
  })

  it('an unaccounted-for property landing in Extensions renders generically and never fails', () => {
    const entry = makeEntry({ extensions: { promoCode: 'SPRING24' } })
    const wrapper = mount(GenericFallbackView, { props: { entry } })
    expect(wrapper.text()).toContain('promoCode')
    expect(wrapper.text()).toContain('SPRING24')
    expect(wrapper.text()).toContain('(Extensions)')
  })

  it('renders ConflictFlag/LateArrivalFlag/AuthorityStatus via the one shared FlagRow convention', () => {
    const entry = makeEntry({ conflictFlag: true, authorityStatus: 'pending_review' })
    const wrapper = mount(GenericFallbackView, { props: { entry } })
    const flagRow = wrapper.getComponent(FlagRow)
    expect(flagRow.props('conflictFlag')).toBe(true)
    expect(flagRow.props('authorityStatus')).toBe('pending_review')
  })

  it('emits retry when the "Retry sync" action is used', async () => {
    const wrapper = mount(GenericFallbackView, { props: { entry: makeEntry() } })
    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })
})
