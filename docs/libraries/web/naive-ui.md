[← Libraries index](../README.md)

# Naive UI (web)

**What it's for:** a Vue 3 component library (tables, forms, layout,
dialogs) with a single, centralized theming API (`themeOverrides`)
applied once at the app root.

**Why bought, not built:** a full component library (accessible,
consistent, themeable widgets) is a huge surface area with no
project-specific value in rebuilding it; the one thing this project does
need to control — a single source of truth for design tokens — is exactly
what `themeOverrides` already provides.

## General usage

```js
export const themeOverrides = {
  common: { primaryColor: '#3B82F6', borderRadius: '4px' },
  DataTable: { thColor: '#f5f5f5' }
}
```

```vue
<n-config-provider :theme-overrides="themeOverrides">
  <App />
</n-config-provider>
```

## Where this project uses it

[The MVVM pattern doc](../../patterns/mvvm-client-architecture.md)'s
**Styling** layer (`src/theme/tokens.js`) — no component overrides its
own colors/spacing locally; every value flows from this one theme
config.

## Links

- [naiveui.com](https://www.naiveui.com/)
