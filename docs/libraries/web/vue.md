[← Libraries index](../README.md)

# Vue 3 (web)

**What it's for:** a reactive JS UI framework — Composition API
composables + reactive refs give MVVM's data/command binding without a
compiled binding engine (unlike WPF/XAML), running natively in any
browser or embedded web engine.

**Why bought, not built:** a reactive rendering/binding runtime is a
large, general problem (dependency tracking, efficient re-render,
component lifecycle) with no project-specific value in reimplementing it
— see [the UI framework shootout](../../comparisons/ui-framework.md) for
the full comparison against Blazor/React/Angular.

## General usage

```vue
<script setup>
import { storeToRefs } from 'pinia'
import { useOrderStore } from '@/stores/orders'
import { useOrderActions } from '@/composables/useOrderActions'

const { orders, loading } = storeToRefs(useOrderStore())
const { loadOrders } = useOrderActions()
onMounted(loadOrders)
</script>
<template>
  <n-data-table :columns="orderColumns" :data="orders" :loading="loading" />
</template>
```

## Where this project uses it

[The MVVM pattern doc](../../patterns/mvvm-client-architecture.md)'s
concrete implementation mapping — the Presentation layer specifically
(`.vue` files); [Pinia](pinia.md) is Data, [Naive UI](naive-ui.md) is
Styling. `ADR-039` is the governing decision this implements.

## Links

- [vuejs.org](https://vuejs.org/)
