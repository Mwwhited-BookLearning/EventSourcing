[← ADR index](../07-adrs.md)

# ADR-087: Internationalization/localization — a framework-level architectural requirement, domain-owned translated content

Status: Accepted

Context: `docs/10-open-questions.md` asked whether i18n/l10n is in or out
of framework scope, given `ADR-073` set the precedent that a UI-cross-
cutting standard (accessibility) belongs at the framework level
regardless of which pattern renders a screen. Direct design conversation
resolved this session: **yes, as a rule** — following `ADR-073`'s exact
precedent (that ADR governs the *requirement*, `ADR-039`/a fallback UI
pattern governs *how it's satisfied*) — **but with a real distinction
`ADR-073` didn't need to draw**: this framework (Duplex) is mostly
backend; the actual UI a given deployment renders is domain/application-
specific (`ADR-030`'s "core engine contains zero domain-specific
knowledge"). So the framework's job is narrower than owning i18n outright
— it's providing **architectural guidance domains build against**, the
same way `ADR-021`'s `EntityId` convention or `ADR-009`'s masking wrapper
are frameworks-provided *shapes* that every domain's own application
logic fills in, not domain content the framework ships itself.

Decision:
- **In framework scope — architectural requirements/conventions, using
  real standards over anything custom, per this session's standing
  principle:**
  - **Locale negotiation via `Accept-Language`** (RFC 9110 §12 — HTTP
    content negotiation), not a bespoke locale query parameter or
    header. The GraphQL Gateway (`ADR-037`) and every `EventStore.
    Host.<Provider>` reads this standard header to select response
    locale where locale-sensitive content is involved.
  - **String externalization is structurally required in `ADR-039`'s
    view-definition format** — a view definition's rendered text must
    reference a translation key, never a hardcoded literal, the same
    structural discipline `ADR-073` already imposes for ARIA attributes.
    This ADR states the requirement; `ADR-039` governs the concrete
    mechanism (a resource-key convention in the HTML/JS view-definition
    shape).
  - **Locale-aware formatting via built-in culture APIs, not hand-rolled
    logic** — `System.Globalization` server-side, the `Intl` API
    (`Intl.DateTimeFormat`, `Intl.NumberFormat`) client-side — for any
    date/number/currency rendering, consistent with `ADR-041`'s first-
    party-over-custom preference.
  - **RTL layout support via CSS Logical Properties** (W3C CSS Logical
    Properties and Values) — `ADR-039`'s base stylesheet conventions use
    logical properties (`margin-inline-start`, not `margin-left`) so a
    domain's view renders correctly in an RTL locale without a second,
    mirrored stylesheet.
- **Out of framework scope — the actual translated strings/content**,
  explicitly domain-owned. This is not a gap; it's the same shape this
  design already has for domain vocabulary — every `docs/domains/
  {domain}/README.md#glossary` is already domain-specific terminology
  the framework never tries to own. Translated content is that same
  domain-owned terminology, in more than one language.
- **This ADR governs the requirement/conventions; `ADR-039` (and any
  fallback UI pattern) governs how they're satisfied** — the identical
  separation `ADR-073` already established, reused rather than
  re-decided.

Consequences:
- `ADR-039`'s view-definition format needs the translation-key
  requirement and logical-properties convention added explicitly —
  flagged as propagation work, not done in this pass.
- No translation-management system (a TMS, a specific resource-file
  format) is adopted here — that's an implementation detail of whichever
  domain/deployment actually needs more than one locale, not a framework
  decision, consistent with translated content being out of scope.
- Resolves `docs/10-open-questions.md` row 19.
