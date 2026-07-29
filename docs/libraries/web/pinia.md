[← Libraries index](../README.md)

# Pinia (web)

**What it's for:** Vue's official state-management library — a store
holds state + getters (derived state), mutated only through actions,
with full reactivity and TypeScript inference.

**Why bought, not built:** cross-component shared reactive state with
correct change propagation is exactly the kind of thing a framework's
own state-management library gets right by construction; a hand-rolled
version would just be a worse Pinia.

## General usage

```js
export const useOrderStore = defineStore('orders', {
  state: () => ({ orders: [], loading: false }),
  getters: {
    pendingOrders: (state) => state.orders.filter(o => o.status === 'pending')
  }
})
```

## Where this project uses it

[The MVVM pattern doc](../../patterns/mvvm-client-architecture.md)'s
**Data** layer (`src/stores/*.js`) — the single source of truth a
[Vue](vue.md) `.vue` file reads via `storeToRefs`, written only by a
composable's command, never mutated directly by the View.

## Links

- [pinia.vuejs.org](https://pinia.vuejs.org/)
