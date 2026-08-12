[← Libraries index](../README.md)

# axe-core (web)

**What it's for:** an automated accessibility-testing engine
([dequelabs/axe-core](https://github.com/dequelabs/axe-core)) that runs
a real ruleset against an actually-rendered DOM and reports WCAG
conformance violations, tagged by which rule/tag (`wcag2a`, `wcag2aa`,
`wcag21a`, `wcag21aa`, ...) each one belongs to.

**Why bought, not built:** WCAG's own success criteria are extensive and
easy to get subtly wrong by hand (color-contrast math, ARIA-role
inference, focus-order heuristics); axe-core is the industry-standard,
zero-false-positive-by-design engine most real accessibility tooling
(browser DevTools, Lighthouse, Playwright's own a11y assertions) already
builds on, rather than a bespoke rule-checker this project would need to
maintain against every future WCAG revision itself.

## General usage

```typescript
import { run } from 'axe-core'

const results = await run(document.body, {
  runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
})

expect(results.violations.filter(v => v.impact === 'critical' || v.impact === 'serious')).toHaveLength(0)
```

**Honest, verified limitation, not glossed over**: axe-core's
`color-contrast` rule needs a real `<canvas>` 2D context internally.
Under jsdom (this project's own unit-test DOM), `HTMLCanvasElement.
getContext` throws "not implemented" — the rule always lands in
`results.incomplete` (impact `"serious"`), never actually determined
pass or fail, confirmed by inspecting `results.incomplete` directly
rather than assuming a clean `results.violations` array means full
coverage. Closed by cross-checking the SAME rendered markup in a real
headless-Chromium harness instead (this project's own repeated "actually
run it in a real browser" discipline), not by installing the native
`canvas` npm package (failed to build in this environment — no Visual
Studio build tools on Windows).

## Where this project uses it

`ADR-073` — `client-web/packages/reference-app/src/a11y.spec.ts` runs axe-core against the
MVVM client's own actually-rendered DOM (`GenericFallbackView`,
`TemplateRenderer`-backed screens, the shared `FlagRow` convention),
asserting zero critical/serious violations across every scenario, plus a
real headless-Edge cross-check specifically for the `color-contrast`
rule's own jsdom gap above.

## Links

- [github.com/dequelabs/axe-core](https://github.com/dequelabs/axe-core)
- [npmjs.com/package/axe-core](https://www.npmjs.com/package/axe-core)
- [W3C WCAG 2.1](https://www.w3.org/TR/WCAG21/) — the standard axe-core checks conformance against
