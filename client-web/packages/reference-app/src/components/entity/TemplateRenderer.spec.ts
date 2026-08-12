import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TemplateRenderer from './TemplateRenderer.vue'
import type { ClientEntityCacheEntry } from '@eventstore/mvvm-client'

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

  // ADR-087 -- {{ t:key }} translation-key resolution, {{ field:date }}/
  // {{ field:number }} locale-aware formatting, and the RTL dir attribute,
  // matching TranslationKeyValidator.cs's own server-side pattern exactly.
  describe('i18n/l10n (ADR-087)', () => {
    it('resolves a {{ t:key }} translation key against the supplied translations map', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="label">{{ t:carrier_label }}</div>',
          entry: makeEntry(),
          translations: { carrier_label: 'Carrier' },
        },
      })
      expect(wrapper.get('.label').text()).toBe('Carrier')
    })

    it('surfaces an unresolved translation key visibly, never silently blanking it', () => {
      const wrapper = mount(TemplateRenderer, {
        props: { templateContent: '<div class="label">{{ t:missing_key }}</div>', entry: makeEntry(), translations: {} },
      })
      expect(wrapper.get('.label').text()).toBe('[missing_key]')
    })

    it('formats a {{ field:number }} binding using Intl.NumberFormat for the given locale', () => {
      const wrapper = mount(TemplateRenderer, {
        props: { templateContent: '<div class="amount">{{ amount:number }}</div>', entry: makeEntry({ data: { amount: 12345.6 } }), locale: 'de-DE' },
      })
      expect(wrapper.get('.amount').text()).toBe(new Intl.NumberFormat('de-DE').format(12345.6))
    })

    it('formats a {{ field:date }} binding using Intl.DateTimeFormat for the given locale', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="placed">{{ placedAt:date }}</div>',
          entry: makeEntry({ data: { placedAt: '2026-08-11T10:00:00.000Z' } }),
          locale: 'en-US',
        },
      })
      expect(wrapper.get('.placed').text()).toBe(new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date('2026-08-11T10:00:00.000Z')))
    })

    it('sets dir="rtl" on the rendered container for an RTL locale, and "ltr" otherwise', async () => {
      const wrapper = mount(TemplateRenderer, {
        props: { templateContent: '<div>{{ orderId }}</div>', entry: makeEntry(), locale: 'ar-SA' },
      })
      expect(wrapper.get('[data-testid="template-container"]').attributes('dir')).toBe('rtl')

      await wrapper.setProps({ locale: 'en-US' })
      expect(wrapper.get('[data-testid="template-container"]').attributes('dir')).toBe('ltr')
    })

    it('defaults to en-US/ltr and an empty translations map when neither prop is supplied', () => {
      const wrapper = mount(TemplateRenderer, {
        props: { templateContent: '<div class="label">{{ t:carrier_label }}</div>', entry: makeEntry() },
      })
      expect(wrapper.get('.label').text()).toBe('[carrier_label]')
      expect(wrapper.get('[data-testid="template-container"]').attributes('dir')).toBe('ltr')
    })
  })

  // A masked field (x-masking, ADR-009), reached over the live subscription
  // path once subscriptionBuilder.ts's own masked-aware selection resolves
  // it, arrives here as the same { value, masked, erased } wrapper server-
  // side masking already produces everywhere else -- never a raw scalar.
  // Before this, String(value) on that object would have rendered the
  // literal text "[object Object]"; found while building the Vitals/
  // Meridian proving-ground samples, since neither domain's own PHI/PII
  // fields had ever been rendered through this path before.
  describe('masked fields (ADR-009)', () => {
    it('renders the real value when a claim holder receives it unmasked', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="name">{{ legalName }}</div>',
          entry: makeEntry({ data: { legalName: { value: 'Jane Smith', masked: null, erased: null } } }),
        },
      })
      expect(wrapper.get('.name').text()).toBe('Jane Smith')
    })

    it('renders the masked placeholder when the caller lacks the required claim', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="name">{{ legalName }}</div>',
          entry: makeEntry({ data: { legalName: { value: null, masked: '***', erased: null } } }),
        },
      })
      expect(wrapper.get('.name').text()).toBe('***')
    })

    it('renders an erased indicator once the field\'s own encryption key has been destroyed (ADR-057), regardless of a stale masked/value branch', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="name">{{ legalName }}</div>',
          entry: makeEntry({ data: { legalName: { value: null, masked: null, erased: true } } }),
        },
      })
      expect(wrapper.get('.name').text()).toBe('(erased)')
    })

    it('prefers a translated erased indicator when one is supplied', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="name">{{ legalName }}</div>',
          entry: makeEntry({ data: { legalName: { value: null, masked: null, erased: true } } }),
          translations: { field_erased: 'Erased' },
        },
      })
      expect(wrapper.get('.name').text()).toBe('Erased')
    })

    it('formats a masked field\'s real, unmasked value with a :date/:number modifier exactly like an ordinary scalar field', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="dob">{{ dateOfBirth:date }}</div>',
          entry: makeEntry({ data: { dateOfBirth: { value: '1990-03-01T00:00:00.000Z', masked: null, erased: null } } }),
          locale: 'en-US',
        },
      })
      expect(wrapper.get('.dob').text()).toBe(new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(new Date('1990-03-01T00:00:00.000Z')))
    })

    it('renders a masked (not revealed) field\'s placeholder text as-is with a :date/:number modifier, never attempting to format it', () => {
      const wrapper = mount(TemplateRenderer, {
        props: {
          templateContent: '<div class="dob">{{ dateOfBirth:date }}</div>',
          entry: makeEntry({ data: { dateOfBirth: { value: null, masked: 'REDACTED', erased: null } } }),
          locale: 'en-US',
        },
      })
      expect(wrapper.get('.dob').text()).toBe('REDACTED')
    })
  })
})
