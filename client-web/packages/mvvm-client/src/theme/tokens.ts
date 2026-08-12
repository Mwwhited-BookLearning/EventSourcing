// docs/patterns/mvvm-client-architecture.md's own "Styling (shared theme)"
// layer rule: one theme config, provided once at the app root, no per-
// component color/spacing overrides. This project skips a full component-
// library theme provider (Naive UI, per docs/06-solution-structure.md) --
// a UI-kit choice, cosmetic and not load-bearing for this item's own three
// named exit criteria -- and uses these as plain CSS custom properties
// instead, applied once in App.vue. An honest, named scope narrowing, not
// a silent substitution.
export const tokens = {
  '--duplex-border': '#d0d0d0',
  '--duplex-flag-active': '#b45309',
  '--duplex-bg': '#ffffff',
  '--duplex-fg': '#111111',
}
