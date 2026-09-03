// Phase 5 (docs/architecture-design-guidelines.md): a general TypeScript
// + Vue baseline, at "warning" severity throughout per direct request --
// eslint-plugin-only-warn force-downgrades every rule from whichever
// config below sets it, rather than hand-mapping hundreds of rule
// severities individually. Covers both npm workspace packages
// (packages/mvvm-client, packages/reference-app) from one root config,
// the normal ESLint flat-config pattern for a monorepo.
import js from "@eslint/js";
import tseslint from "typescript-eslint";
import pluginVue from "eslint-plugin-vue";
import onlyWarn from "eslint-plugin-only-warn";
import globals from "globals";

export default [
  {
    ignores: [
      "**/dist/**",
      "**/dist-*/**",
      "**/node_modules/**",
      "**/coverage/**",
      "**/*.d.ts",
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...pluginVue.configs["flat/recommended"],
  {
    languageOptions: {
      globals: {
        ...globals.browser,
        ...globals.node,
      },
      parserOptions: {
        // Vue SFC <script> blocks parsed via typescript-eslint's parser,
        // the standard eslint-plugin-vue + typescript-eslint combination.
        parser: tseslint.parser,
      },
    },
    plugins: {
      "only-warn": onlyWarn,
    },
  },
];
