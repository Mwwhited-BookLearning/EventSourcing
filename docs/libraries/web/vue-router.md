[← Libraries index](../README.md)

# Vue Router (web)

**What it's for:** the official Vue 3 client-side router — URL-driven
route matching, nested routes, navigation guards.

**Why bought, not built:** `client-web` had gotten by without one
because it started as a single generic entity view (`ADR-039`); once it
grew to eight real tabs across two proving-ground domains
(`docs/playbooks/README.md`), a hand-rolled `activeTab` ref switcher was
no longer meaningfully different from routing except for lacking a URL,
deep-linking, or a navigation-guard hook — all things this library
already does correctly rather than reinventing.

## General usage

```js
const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/browse', component: EntityBrowser },
    { path: '/queue', component: QueuePanel, meta: { requiresDomain: true } }
  ]
})

router.beforeEach((to) => {
  if (to.meta.requiresDomain && !queueDomainForCurrentApp()) return '/detail'
})
```

## Where this project uses it

[`ADR-099`](../../adrs/adr-099-naive-ui-router-left-nav-shell.md) — one
route per existing tab (Detail/Browse/Composer/Queue/Relying-Party/
Lineage) behind the new Naive UI left-hand-nav shell, replacing the
prior router-free `activeTab` tab switcher. The domain-gating a
navigation guard now performs was previously a template `v-if` on
`App.vue`'s own `queueDomain` computed.

## Links

- [router.vuejs.org](https://router.vuejs.org/)
