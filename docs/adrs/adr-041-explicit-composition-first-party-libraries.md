[← ADR index](../07-adrs.md)

# ADR-041: Explicit composition and first-party libraries over convention-magic

Status: Accepted

Context: Direction received this session, across several related
statements: prefer constructor injection and explicit/manual composition
over convention-driven "magic"; prefer Microsoft's own first-party
libraries over third-party equivalents for configuration, DI, and
logging; prefer `System.Text.Json` over `Newtonsoft.Json`. Read together,
these are one coherent principle — favor explicit, traceable composition
and first-party framework pieces over reflection-driven convention and
third-party abstractions — applied consistently across several concerns
that would otherwise each need their own bespoke justification.

Decision:
- **Constructor injection, everywhere it's possible.** No property
  injection, no method injection, no service-locator lookups
  (`IServiceProvider.GetService<T>()` reached for from inside arbitrary
  code) where a constructor parameter would do. A type's dependencies are
  fully visible in its constructor signature — nowhere else.
- **Manual, explicit composition over assembly-scanning auto-registration
  — "Pure DI" / Composition Root** (Mark Seemann, *Dependency Injection
  Principles, Practices, and Patterns*; [blog.ploeh.dk — Composition
  Root](https://blog.ploeh.dk/2011/07/28/CompositionRoot/)): every
  service registration is a visible, explicit
  `services.AddScoped<IFoo, Foo>()`-style line in a composition root
  (each `EventStore.Host.<Provider>`'s `Program.cs`, per
  `06-solution-structure.md`), not a reflection-based scan that infers
  registrations from naming conventions or assembly contents. The
  container itself stays `Microsoft.Extensions.DependencyInjection` —
  Microsoft's own, already load-bearing throughout this design
  (`ADR-006`, `ADR-026`'s Aspire `ServiceDefaults`) — what's rejected is
  *convention-magic on top of it*, not the container itself.
- **`Microsoft.Extensions.Logging` remains the one logging abstraction**
  (already implicit in `ADR-026`'s Aspire+OpenTelemetry decision) — no
  third-party structured-logging framework (Serilog, NLog, log4net)
  is introduced alongside or underneath it. Aspire's `ServiceDefaults`
  already wires OpenTelemetry directly into
  `Microsoft.Extensions.Logging`; adding a second logging framework
  would duplicate, not improve, that pipeline.
- **No AutoMapper (or any reflection/convention-based object-mapping
  library).** Any type-to-type mapping this design needs (event payload
  ↔ read model, `StoredEvent` ↔ API response shape) is a small, explicit,
  hand-written mapping method — visible in code review, debuggable with a
  normal breakpoint, and with none of AutoMapper's "which convention
  matched which property" indirection to reason about when a mapping is
  wrong.
- **`System.Text.Json` over `Newtonsoft.Json`** for all JSON
  serialization — first-party, part of the runtime since .NET Core 3.0,
  already the default ASP.NET Core uses; nothing in this design has
  needed a `Newtonsoft.Json`-specific feature (custom `JsonConverter`
  needs, where they arise, are equally expressible against
  `System.Text.Json`'s converter model).
- **Configuration stays `Microsoft.Extensions.Configuration`** — the
  first-party, framework-native configuration system
  (`appsettings.json`/environment variables/command line), not a
  third-party alternative — this was never actually in tension with
  "prefer first-party," since `Microsoft.Extensions.*` **is** the
  first-party choice; the object of this ADR is convention-magic and
  genuinely third-party libraries layered on top, not Microsoft's own
  framework packages.
- **Addendum — production secrets management (resolves `docs/10-open-
  questions.md`'s row directly, no new ADR): standard configuration
  providers, not a bespoke secrets mechanism.** Direction received this
  session: secrets are handled through standard configuration
  frameworks. This is `Microsoft.Extensions.Configuration` again, not a
  new decision — every secret this design needs (DB connection strings,
  `ADR-057`'s KEK reference, `ADR-040`/`ADR-060`'s HMAC signing secrets,
  any KMS credentials) is an ordinary configuration value, supplied by
  whichever first-party or provider-native configuration source a
  deployment already uses: environment variables, `dotnet user-secrets`
  for local dev, or a configuration provider backed by the deployment's
  own secret store (Azure Key Vault, AWS Secrets Manager, HashiCorp
  Vault all have `Microsoft.Extensions.Configuration`-compatible
  provider packages, each usable cloud-hosted or self-hosted/on-prem).
  This framework itself adopts none of those providers as a hard
  dependency — `IConfiguration` is the only surface it depends on,
  consistent with `ADR-057`'s `IErasureKeyStore` being keyed/pluggable
  per `AppId` rather than tied to one vendor. **Combinations already
  work with no further design needed**: `Microsoft.Extensions.
  Configuration` is built to chain multiple providers in one pipeline
  (environment variables + a Key Vault provider + a self-hosted Vault
  provider, layered, later sources overriding earlier ones) — cloud,
  on-prem, and local secrets sources can already be mixed in one
  deployment today, the same requirement `ADR-057`'s amendment states
  explicitly for `IErasureKeyStore`.

Consequences:
- **No rework elsewhere**: nothing previously accepted in this design
  adopted AutoMapper, Newtonsoft.Json, or a third-party logging
  framework, so this ADR closes off a class of future choice rather than
  reversing one already made. `ADR-026` (Aspire/OpenTelemetry) is
  unaffected and fully consistent with this decision.
- A composition root that lists every registration explicitly grows
  linearly with the number of services — accepted, deliberately, as the
  cost of always being able to answer "where does this get registered"
  by reading one file rather than trusting a naming convention to have
  matched correctly.
- Hand-written mapping code has a real, known cost relative to
  AutoMapper for a type with many properties (more lines, a mapping to
  keep in sync by hand when a property is added) — accepted the same way
  `references.md` already accepts costs for other "explicit over
  general/magic" choices in this design (e.g. `ADR-018`'s upcast mapping
  choosing a narrower expression language over a more powerful one).
