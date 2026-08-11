import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import FlagRow from './FlagRow.vue'

describe('FlagRow (the one shared ConflictFlag/LateArrivalFlag/AuthorityStatus convention, ADR-024/029/035)', () => {
  it('renders all three via the same component, not three bespoke indicators', () => {
    const wrapper = mount(FlagRow, {
      props: { conflictFlag: true, lateArrivalFlag: false, authorityStatus: 'pending_review' },
    })

    expect(wrapper.get('[data-testid="conflict-flag"]').text()).toContain('ConflictFlag')
    expect(wrapper.get('[data-testid="late-arrival-flag"]').text()).toContain('LateArrivalFlag')
    expect(wrapper.get('[data-testid="authority-status"]').text()).toContain('pending_review')
  })

  it('marks an active flag distinctly from an inactive one, still through the same convention', () => {
    const wrapper = mount(FlagRow, {
      props: { conflictFlag: true, lateArrivalFlag: false, authorityStatus: 'accepted' },
    })

    expect(wrapper.get('[data-testid="conflict-flag"]').classes()).toContain('flag--active')
    expect(wrapper.get('[data-testid="late-arrival-flag"]').classes()).not.toContain('flag--active')
  })
})
