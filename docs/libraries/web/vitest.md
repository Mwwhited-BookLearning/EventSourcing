[← Libraries index](../README.md)

# Vitest (web)

**What it's for:** a Vite-native unit test runner — Jest-compatible
assertion/mocking API, but reuses the Vue app's own Vite config/
transforms/resolvers directly, so there's zero separate test-bundler
configuration to maintain alongside the app's real build.

**Why bought, not built:** maintained by the Vue/Vite team itself and
has solidified as the standard unit-test runner for Vite-based projects
— exactly the "same vendor on both ends" reasoning `ADR-054` already
applied to `Strawberry Shake`/`HotChocolate`, here applied to
`Vitest`/`Vite` (`ADR-039`'s client is already Vite-based).

## General usage

```typescript
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import OrderTable from '@/components/OrderTable.vue'

describe('OrderTable', () => {
  it('renders a row per order', () => {
    const wrapper = mount(OrderTable, { props: { orders: [{ id: '1' }] } })
    expect(wrapper.findAll('tr')).toHaveLength(1)
  })
})
```

## Where this project uses it

`ADR-055` — the unit-test runner for `ADR-039`'s Vue/Pinia client,
alongside `@vue/test-utils` (`docs/libraries/web/vue-test-utils.md`).

## Links

- [vitest.dev](https://vitest.dev/)
- [github.com/vitest-dev/vitest](https://github.com/vitest-dev/vitest)
