[← Libraries index](../README.md)

# Vue Test Utils (web)

**What it's for:** Vue's own official low-level component-testing
library — `mount`/`shallowMount` a `.vue` component in isolation,
interact with it (`trigger`, `setValue`), and assert on rendered output
or emitted events.

**Why bought, not built:** Vue's own components render through a
reactivity/virtual-DOM runtime that isn't something a test author should
reimplement just to mount a component in a test — the official library
already handles it correctly.

## General usage

```typescript
import { mount } from '@vue/test-utils'
import OrderTable from '@/components/OrderTable.vue'

const wrapper = mount(OrderTable, { props: { orders: [] } })
await wrapper.find('button.refresh').trigger('click')
expect(wrapper.emitted('refresh')).toBeTruthy()
```

## Where this project uses it

`ADR-055` — component-level unit tests for `ADR-039`'s Vue/Pinia client,
run under `Vitest` (`docs/libraries/web/vitest.md`).

## Links

- [test-utils.vuejs.org](https://test-utils.vuejs.org/)
