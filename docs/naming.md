[← Document index](../README.md)

# Naming

Company and product names for this framework and its proving-ground
applications. Distinct from every other document kind in this package —
not a decision about the system's architecture, just what it's called.
Kept separate from `README.md`/`CLAUDE.md` so naming churn (a rename, a
new proving-ground product) doesn't touch the architecture-decision
history around it.

**Not a trademark clearance.** Every name below was checked only for
obvious, known collisions (a quick mental/web scan for an identical
product in an adjacent space) — not a real trademark search. Run one
before committing to any of these in a public release.

## Company

**OoBDev** — Out of Band Development.

"Out-of-band" is also a real signaling term: a channel carried alongside
a primary one, carrying control/verification information rather than
payload. That happens to describe a fair amount of what this framework
already does — non-authoritative capture (`ADR-035`), streaming channels
(`ADR-031`), ticket exchange for header-incapable clients (`ADR-040`) —
so product names lean on a related **navigation/signaling** theme rather
than being unrelated made-up words.

## Base event-sourcing engine

**Duplex**

A full-duplex channel carries two directions of traffic simultaneously —
a fitting name for an engine whose entire reason for existing is the
write/read (command/query) split (`ADR-015`/`ADR-016`, `09-cqrs-read-
models.md`). Reads as a plausible real infra-product name (in the
company of things like Kafka, Temporal) rather than a cute pun.

Alternates considered, not chosen:

| Name | Why it was in the running | Why `Duplex` won out |
|---|---|---|
| Sideband | Most literal tie to "out of band" — an auxiliary signal channel alongside a main one | `Duplex` names the CQRS split directly; `Sideband` only echoes the company name |
| Keelson | A ship's structural backbone, reinforcing the keel — apt for an immutable backbone other systems build on | Reads more nautical-poetic than product-plain |
| Chronicle | Plain-spoken, evokes an append-only historical record | Very literal for event sourcing, but a common name already used by several unrelated products |
| Wayline | "Way" (course/direction) + "line" (timeline/log) | Softer sound, less distinctive than `Duplex` |

## Clinical trials + connected medical-device telemetry

**Vitals**

Directly evokes patient vital signs and continuous device telemetry —
the literal subject matter of this proving-ground domain
(`docs/domains/clinical-trials-device-telemetry/README.md`) — without needing
an invented word.

Alternates considered, not chosen:

| Name | Why it was in the running | Why `Vitals` won out |
|---|---|---|
| Longitude | Double meaning: navigation term (stays in the `Duplex`/`Meridian` family) *and* "longitudinal" is the literal word for a multi-year clinical trial | Clever, but reads more abstract than `Vitals`' plain-spoken clarity |
| Sentinel | "Sentinel event" is an actual term of art in clinical patient-safety/quality reporting | Heavily used elsewhere already (SentinelOne, Microsoft Sentinel) — high collision risk |

## Digital identity / KYC

**Meridian**

A meridian is a reference line you verify position against — a fitting
metaphor for identity attestation and verification
(`docs/domains/digital-identity-kyc/README.md`), and it keeps the
navigation-metaphor family going alongside `Duplex`.

Alternates considered, not chosen:

| Name | Why it was in the running | Why `Meridian` won out |
|---|---|---|
| Anchor | Grounding/trust metaphor, stays in the nautical family | Extremely common product name across unrelated industries — high collision risk |
| Warrant | Double meaning: legal warrant/attestation, and "I can warrant this is true" | Strong meaning fit, but breaks the navigation-metaphor family |
| Vouch | Most literal ("I vouch for this identity") | Highest collision risk of the set — existing identity-verification companies already use "Vouch"/"Vouched" |

## Not yet named

The other 13 considered-but-not-chosen proving-ground domains
(`docs/domains/README.md`) have no product name — naming is only useful
for the two domains actually being built. Revisit if/when a third
proving-ground application gets built.
