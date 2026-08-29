// docs/patterns/mvvm-client-architecture.md's own "Styling (shared theme)"
// layer rule: one theme config, provided once at the app root, no per-
// component color/spacing overrides. ADR-099 landed the full Naive UI
// component-library theme provider (`n-config-provider`, App.vue) -- these
// plain CSS custom properties remain, still applied once in App.vue, for
// scoped-style rules in components that predate that adoption and for
// `themeOverrides` below to read from, one source of truth either way.
export const tokens = {
  '--duplex-border': '#d0d0d0',
  '--duplex-flag-active': '#b45309',
  '--duplex-bg': '#ffffff',
  '--duplex-fg': '#111111',
}

// Naive UI's own theme-provider shape (ADR-099) -- derived from the same
// tokens above, not a second, independent set of values.
export const themeOverrides = {
  common: {
    borderColor: tokens['--duplex-border'],
    bodyColor: tokens['--duplex-bg'],
    textColorBase: tokens['--duplex-fg'],
  },
}
