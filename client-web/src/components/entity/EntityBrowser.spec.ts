import { describe, expect, it, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import EntityBrowser from './EntityBrowser.vue'
import { useEntityCacheStore } from '../../stores/entityCache'
import { resetDbConnectionForTests } from '../../db/indexedDb'

describe('EntityBrowser', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetDbConnectionForTests()
  })

  it('shows a message when nothing is cached yet', () => {
    const wrapper = mount(EntityBrowser, { props: { instanceId: 'instance-a' } })
    expect(wrapper.text()).toContain('No entities cached yet')
  })

  it('lists every distinct cached entity for this instance and emits select on View', async () => {
    const store = useEntityCacheStore()
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-1', {
      orderId: 'o-1',
      amount: 150,
      conflictFlag: false,
      lateArrivalFlag: false,
      authorityStatus: 'accepted',
      schemaVersion: 1,
    })
    await store.applyFollowedEvent('instance-a', 'orderplaced', 'mvvm-demo:orderplaced:o-2', {
      orderId: 'o-2',
      amount: 200,
      conflictFlag: false,
      lateArrivalFlag: false,
      authorityStatus: 'accepted',
      schemaVersion: 1,
    })

    const wrapper = mount(EntityBrowser, { props: { instanceId: 'instance-a' } })
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)

    await wrapper.findAll('button')[0]!.trigger('click')
    expect(wrapper.emitted('select')).toBeTruthy()
  })
})
