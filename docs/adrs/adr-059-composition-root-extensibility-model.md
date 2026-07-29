[← ADR index](../07-adrs.md)

# ADR-059: Extensibility is composition-root registration only — no dynamic/runtime plugin discovery, ever

Status: Accepted

Context: `docs/10-open-questions.md` asked whether this framework should
publish a consolidated "Extensibility Points" reference, given it already
has a real, if scattered, set of plugin seams (`IMaskingStrategy`,
`IUpcastExpressionEvaluator`, `IStreamRedactionStrategy`, `IProjection<T>`,
`IEventUpcaster`, the per-provider `IEventLineageQueryProvider`/
`IJsonPathTranslator` adapters — soon `IErasureKeyStore`, `ADR-057`).
Direction received this session on how local extensions should work:
"it would be on the hosting team to import the extension into the IoC" —
i.e., a hosting/deployment team writes a class implementing the relevant
interface and registers it themselves, in their own composition root.

Decision:
- **Formalized, not new**: this is exactly `ADR-041`'s existing Pure
  DI/Composition Root decision, restated as the answer to "how do I add
  an extension" specifically, so it's discoverable as such rather than
  only inferable from `ADR-041`'s general DI stance. There is, and will
  never be, a dynamic plugin-discovery mechanism (assembly scanning,
  reflection-based auto-registration, a runtime plugin manifest/registry
  in the shape of Eclipse's Extension Points, `MEF`, or similar) anywhere
  in this framework.
- **Every extension point follows the identical shape**: an interface
  (e.g. `IMaskingStrategy`), one or more built-in implementations
  registered in the framework's own composition root, and a hosting
  team's custom implementation registered the same way in *their*
  composition root — a visible `services.AddKeyedSingleton<IMaskingStrategy,
  MyCustomStrategy>("MyStrategy")`-shaped line, not a dropped-in DLL
  discovered automatically. Adding an extension is always "write a class,
  add a registration line," never "drop a file in a folder and it's
  picked up."
- **This is a deliberate ceiling, not an oversight**: a hosting team that
  wants to add a masking strategy, upcast engine, projection, or erasure
  key backend always edits their own `Program.cs`-equivalent composition
  root. There is no supported path for loading extension code the
  framework's own deployment didn't explicitly reference at build/publish
  time — consistent with `ADR-001`'s existing "one artifact per provider,
  chosen at build time" posture and `ADR-041`'s rejection of convention-
  magic generally.

Consequences:
- Resolves the "local extensions" half of `docs/10-open-questions.md`'s
  extensibility-cataloging row. The "should there be a consolidated
  reference" half is answered by writing one:
  [`docs/extensibility-points.md`](../extensibility-points.md) catalogs
  every seam above in one place, each pointing back to the ADR that
  defines it, with this ADR's registration model stated once at the top
  rather than repeated per seam.
- The outbound half of that review's finding — webhook/notification
  support — is a distinct question (a new outbound integration surface,
  not a registration-model question) and is resolved separately in
  `ADR-060`.
- No code changes anywhere — every existing seam already follows this
  shape; this ADR names the pattern explicitly and rules out ever adding
  a second, dynamic extension model alongside it.
