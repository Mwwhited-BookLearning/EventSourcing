import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TemplateRenderer from './TemplateRenderer.vue'
import type { ClientEntityCacheEntry } from '../../types'

function makeEntry(overrides: Partial<ClientEntityCacheEntry> = {}): ClientEntityCacheEntry {
  return {
    entityId: 'mvvm-demo:orderplaced:o-1',
    instanceId: 'instance-a',
    entityType: 'orderplaced',
    data: { orderId: 'o-1', amount: 150 },
    extensions: {},
    schemaVersion: 1,
    conflictFlag: false,
    lateArrivalFlag: false,
    authorityStatus: 'accepted',
    cachedAt: new Date().toISOString(),
    ...overrides,
  }
}

describe('TemplateRenderer (ADR-039\'s "small injected binding runtime")', () => {
  it('interpolates {{ field }} placeholders from the entry\'s own Data', () => {
    const wrapper = mount(TemplateRenderer, {
      props: { templateContent: '<div class="order">Order {{ orderId }}: ${{ amount }}</div>', entry: makeEntry() },
    })
    expect(wrapper.get('.order').text()).toBe('Order o-1: $150')
  })

  it('re-renders when the entry changes, without precompiling the template', async () => {
    const wrapper = mount(TemplateRenderer, {
      props: { templateContent: '<div class="amount">{{ amount }}</div>', entry: makeEntry({ data: { amount: 150 } }) },
    })
    expect(wrapper.get('.amount').text()).toBe('150')

    await wrapper.setProps({ entry: makeEntry({ data: { amount: 175 } }) })
    expect(wrapper.get('.amount').text()).toBe('175')
  })

  it('dispatches a command when a data-command-field element is clicked, reading the paired input', async () => {
    const wrapper = mount(TemplateRenderer, {
      props: {
        templateContent: '<input class="amount-input" value="200"/><button class="save" data-command-field="Amount" data-command-value-from=".amount-input">Save</button>',
        entry: makeEntry(),
      },
    })

    await wrapper.get('.save').trigger('click')

    expect(wrapper.emitted('command')).toEqual([['Amount', '200']])
  })

  it('renders the shared flag convention alongside the template-bound content', () => {
    const wrapper = mount(TemplateRenderer, {
      props: { templateContent: '<div>{{ orderId }}</div>', entry: makeEntry({ conflictFlag: true }) },
    })
    expect(wrapper.text()).toContain('ConflictFlag')
  })
})
