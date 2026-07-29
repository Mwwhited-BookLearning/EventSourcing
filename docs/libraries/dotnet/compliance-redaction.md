[← Libraries index](../README.md)

# Microsoft.Extensions.Compliance.Redaction (dotnet)

**What it's for:** a first-party .NET library for classifying data
sensitivity (`DataClassification`) and automatically redacting classified
values before they reach a sink — most commonly logs. Ships alongside
`Microsoft.Extensions.Telemetry`, which wires it directly into
`Microsoft.Extensions.Logging`.

**Why bought, not built:** getting redaction genuinely reliable (never
missing a classified field, never leaking through string
interpolation) is exactly the kind of thing worth a first-party,
well-tested library rather than a bespoke scrubbing layer — and it's
already in the same `Microsoft.Extensions.*` family `ADR-041` already
prefers throughout this design.

## General usage

Static, attribute-based (this framework's own internal log call sites):

```csharp
public static class Classifications
{
    public static DataClassification Phi => new("EventStore", nameof(Phi));
}

[LoggerMessage(0, LogLevel.Information, "Reviewed claim for actor {ActorId}")]
public static partial void LogReview(this ILogger logger, [Classifications.Phi] string actorId);
```

```csharp
services.AddLogging(b => b.EnableRedaction());
services.AddRedaction(b => b.SetRedactor<HmacRedactor>(Classifications.Phi));
```

Dynamic, programmatic (schema-driven `Payload` values — no compile-time
property to attribute):

```csharp
var redactor = redactorProvider.GetRedactor(classificationFromRegistry);
var safeValue = redactor.Redact(rawPayloadValue);
logger.LogInformation("Field value: {Value}", safeValue);
```

## Where this project uses it

- `ADR-050` — reusing `ADR-009`'s existing `x-masking` classification
  metadata (`regulatoryClassification`/`requiredClaim`) to prevent
  PII/PHI/PCI from reaching logs, a sink `ADR-009`'s original
  query/stream-response-only masking never covered.
- `ADR-009`'s `"Hash"` masking strategy — `x-masking`'s `masked` value,
  when `strategy` is `"Hash"`, is computed with this same library's
  `HmacRedactor`, not a second hashing mechanism. A bare/unsalted hash of
  a small value space (e.g. a 9-digit SSN) is trivially reversible by
  precomputing every possibility; `HmacRedactor`'s key-based approach
  isn't. `keyId` in the `x-masking` config identifies which configured
  key was used, the same way `HmacRedactorOptions` already requires a
  key for log redaction — one key-management surface, two consumers.

## Links

- [learn.microsoft.com/dotnet/core/extensions/data-redaction](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction)
- [nuget.org/packages/Microsoft.Extensions.Compliance.Redaction](https://www.nuget.org/packages/Microsoft.Extensions.Compliance.Redaction)
