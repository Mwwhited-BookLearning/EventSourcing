[← Comparisons index](README.md)

# Trust-Root Registration Gate: Scope-Gated vs. Delegated vs. Dual-Control

**Raised by:** `ADR-044`'s own Consequences section, flagged to
`docs/10-open-questions.md`: "Who may register/deregister an
`AppTrustRoot` entry? Registering the wrong DID as trusted for an
`AppId` grants that DID's holder the ability to mint arbitrary
permissions within that application's namespace."

**Stated requirement driving this comparison:** registering an
`AppTrustRoot` is not an ordinary write — the DID it names becomes the
root of a UCAN delegation chain (`ADR-036`) authoritative for minting
*any* custom permission string within that `AppId`'s namespace
(`ADR-044`). Getting this gate wrong doesn't just leak one claim; it
hands an attacker (or a careless operator) the ability to self-issue
arbitrary permissions for an entire application, indefinitely, until the
bad entry is caught and de-registered. This is a privilege-escalation-risk
question first, an access-control-mechanics question second.

## Prior art

Real systems that gate a comparably sensitive "establish a new root of
trust" action, checked before designing anything bespoke:

- **AWS IAM** — `iam:CreateOpenIDConnectProvider`/`iam:CreateSAMLProvider`
  create the trust anchor a role's trust policy can name as principal;
  AWS's own guidance singles these out as needing restriction "to highly
  privileged users" separately from ordinary account administration,
  precisely because the action it gates is establishing external trust,
  not managing existing resources.
  ([AWS IAM API reference](https://docs.aws.amazon.com/IAM/latest/APIReference/API_CreateOpenIDConnectProvider.html))
- **Google Cloud IAM** — Workload Identity Federation requires the
  dedicated `roles/iam.workloadIdentityPoolAdmin` predefined role to
  create a pool/provider; the broader `roles/owner` basic role also
  happens to include it, but GCP's own guidance treats granting `owner`
  in production as itself the anti-pattern, not the intended path to this
  permission.
  ([Google Cloud — Workload Identity Federation](https://docs.cloud.google.com/iam/docs/workload-identity-federation))
- **Microsoft Entra ID (Azure AD)** — configuring a federated identity
  credential/direct federation with an external IdP has its own
  least-privileged role, **External Identity Provider Administrator**,
  distinct from (and narrower than) Global Administrator, which is
  reserved for domain-level federation specifically.
  ([Microsoft Learn — Add federation with SAML/WS-Fed identity providers](https://learn.microsoft.com/en-us/entra/external-id/direct-federation))
- **NIST SP 800-53 Rev. 5, AC-5 (Separation of Duties)** — the general
  control this design's own RBAC choice (`ADR-046`) already partially
  adopts (base tier only): it "addresses the potential for abuse of
  authorized privileges and helps to reduce the risk of malevolent
  activity without collusion."
  ([CSF Tools — AC-5](https://csf.tools/reference/nist-sp-800-53/r5/ac/ac-5/))
- **PKI root/intermediate CA key ceremonies** — the closest real analogue
  to "minting a root of trust" as a single physical event: a witnessed
  procedure run under documented **dual control, split knowledge, and
  M-of-N quorum** (commonly 3-of-7 or similar), specifically because a
  root CA key, once generated, anchors an entire trust hierarchy and a
  single corrupt operator acting alone is exactly the failure mode the
  ceremony exists to rule out.
  ([Encryption Consulting — Root CA Key Ceremony](https://www.encryptionconsulting.com/education-center/root-ca-key-ceremony/),
  [APNIC Blog — What is "M of N"](https://blog.apnic.net/2021/05/28/what-is-m-of-n-in-public-private-key-signing/))
- **The Four Eyes Principle / two-person rule / dual control** — already
  researched and cited (not adopted) for `ADR-043`, correctly
  disambiguated there as *not* the mechanism a one-person delegation
  needs. This project's own prior conclusion doesn't automatically carry
  over here — see Option C below for why this action's shape is
  different.
  ([goteleport.com — Four Eyes Principle](https://goteleport.com/blog/four-eyes-principle/))

Every cloud IAM analogue above converges on the same shape: a **narrow,
separately-named permission**, not bundled into generic admin — none of
AWS, GCP, or Azure require joint/dual approval for this specific action
in their standard (non-ceremony) IAM model. Only the PKI root-CA case —
which mints a root that's catastrophic and *irreversible* if compromised,
at the scale of an entire certificate hierarchy — actually practices
M-of-N/dual control as standard operating procedure.

## The options

### Option A — A new, narrower `registry:trust-admin` scope

`registry:admin` (`ADR-006`) does **not** automatically include
`AppTrustRoot` registration. A separate scope, `registry:trust-admin`,
is required, checked the same way every other scope-gated endpoint
already is (`ADR-006`'s per-scope `IAuthorizationHandler` policies,
`ADR-046`'s RBAC resolving it into whichever role(s) legitimately need
it).

| | |
|---|---|
| **Pros** | Zero new mechanism — reuses `ADR-006`'s scope model and `ADR-046`'s RBAC wholesale; registering a trust root is checked with the exact same `HasClaim` pattern every other claim in this design already uses. Matches the real-world convergence above: AWS, GCP, and Azure all gate this exact class of action ("establish a new external trust anchor") behind a dedicated, narrowly-named permission distinct from generic admin — this is the industry-standard shape, not a novel invention. Satisfies `NIST SP 800-53 AC-5`'s separation-of-duties intent directly: the population of people who can administer ordinary registry/schema-registration concerns (`registry:admin`) is no longer automatically the same population who can mint a root of trust for an `AppId`'s entire permission namespace. |
| **Cons** | A scope check gates *who is allowed to act*, not *whether the specific act about to happen is correct* — a single holder of `registry:trust-admin`, having authenticated legitimately, can still register the wrong DID by mistake or malice with nothing else stopping them at the moment of the call. Narrowing the population reduces the *number* of people who can cause the worst-case outcome; it doesn't add a check on any individual instance of the action itself. |

### Option B — Trust-root registration as a UCAN-delegatable capability (reuse `ADR-043`'s model)

The ability to register/deregister an `AppTrustRoot` for one specific
`AppId` is itself delegatable, the same additive-only, capped, revocable
shape `ADR-043` already built for "secondary opinion" access grants —
e.g. the central platform operator (holding `registry:trust-admin`
globally) delegates a capability scoped to one `AppId` to that
application's own operations team, capped at the delegator's own level
and revocable the same way `ADR-044` already revokes a trust root
(de-registration stops future validation; already-exchanged JWTs keep
their given lifetime).

| | |
|---|---|
| **Pros** | Fits this design's actual multi-tenant shape (`ADR-030`) — a central operator shouldn't need to personally register every tenant application's trust root by hand, and delegation lets that scale to per-`AppId` operators without widening who holds the *global* `registry:trust-admin` scope. No second delegation mechanism needed — same UCAN attenuation invariant, same exchange flow, same revocation story `ADR-043`/`ADR-044` already specify. |
| **Cons** | This composes with a gate, it isn't one on its own: UCAN's delegation-cap invariant only holds once a first, non-delegated holder of the capability already exists — resolving *that* first grant is exactly the bootstrapping question this comparison exists to answer, and delegation can't answer it about itself (the same out-of-band-root problem `ADR-044`'s own Context section says the UCAN spec deliberately leaves unresolved, one level up). And a delegate, once holding the capability, has exactly the same single-actor risk as Option A's scope holder — delegation changes *how* someone comes to hold the capability, not whether holding it alone is enough to act. |

### Option C — Dual-control / N-person approval for this one action

Two distinct, authorized `registry:trust-admin` holders must both
approve a specific `AppTrustRoot` registration/de-registration before it
takes effect — the classical **Four Eyes Principle** / M-of-N quorum,
the actual mechanism `ADR-043` researched and correctly did *not* adopt
for delegated access grants (a different mechanism there: peer-granted
delegation, not joint approval).

| | |
|---|---|
| **Pros** | This is arguably the one place in this entire design where dual control's actual precondition is genuinely met: **one discrete, high-stakes, hard-to-reverse-in-effect action**, decided once, not an ongoing delegation relationship — exactly the shape Four Eyes/PKI key-ceremony quorum exists for, unlike `ADR-043`'s case (correctly disambiguated as one person *unilaterally* extending access to another, which dual control was never meant to gate). If any action in this design deserves the "isn't this just Four Eyes?" question asked again in earnest, it's this one — not `ADR-043`'s. |
| **Cons** | A genuinely new mechanism this design has nowhere else — a pending-approval state, a second event pair (`trustRootRegistrationRequested`/`...Approved`), and a UI/workflow story for "who is the second approver and how do they get notified" are all real, unbuilt engineering, not glue between two existing primitives the way Options A and B are. Needs an explicit answer for small/dev deployments with only one authorized admin (this project's own `EventStore.DevIdp`, `ADR-006`, seeds exactly one `operator-client` — a mandatory second approver has no one to be, out of the box). Disproportionate to this design's *actual* blast radius: unlike a PKI root CA (compromise cascades across an entire certificate hierarchy, often un-revocable in practice once trusted broadly) or a cloud account's IAM trust policy (compromise can span every resource in the account), a compromised `AppTrustRoot` is scoped to exactly one `AppId`'s own custom, opaque permission strings (`ADR-044`) — it cannot mint `registry:admin`, cannot touch another `AppId`'s namespace, and cannot forge the central IdP's own operational scopes (`ADR-006`). The contained blast radius is a real, structural reason this design's default risk tolerance doesn't need ceremony-grade quorum for every registration, not just an excuse to skip it. |

## Recommendation

**Option A as the mandatory baseline gate — a new `registry:trust-admin`
scope, deliberately not implied by `registry:admin` — with Option B
available for how it composes with this design's multi-tenant shape, and
Option C explicitly not adopted as a system-wide requirement.**

Every cloud IAM system checked above (AWS, GCP, Azure) converges on
Option A's shape for exactly this class of action, and it costs nothing
new to build — `ADR-006`'s scope model and `ADR-046`'s RBAC already do
all the work; this only adds one more opaque scope string to the seeded
set. `registry:admin` holders do not get `registry:trust-admin` for
free, satisfying `NIST SP 800-53 AC-5`'s separation-of-duties intent
between "administers the registry" and "can mint a root of trust for an
application's entire permission namespace." Option B composes on top,
unchanged from `ADR-043`/`ADR-044`'s existing delegation shape, so a
central operator can extend a capped, `AppId`-scoped, revocable slice of
`registry:trust-admin` to an application's own team without widening the
global scope population — this is additive, not a new decision.

Option C is a closer real fit here than it was for `ADR-043` — this
comparison says so plainly rather than reflexively re-applying the
earlier rejection — but is still not adopted as the default, for a
reason specific to *this* design rather than a general dismissal of dual
control: `AppTrustRoot`'s blast radius is structurally contained to one
`AppId`'s own custom capability namespace, not the whole system's trust
root the way a PKI root CA or an AWS account's federated-identity trust
policy is. That containment is the actual reason ceremony-grade M-of-N
approval is disproportionate as a system-wide requirement, not an
assumption that the risk is unreal. A deployment whose own risk
tolerance genuinely calls for it — an `AppId` carrying regulated or
safety-critical permissions, for instance — can layer Option C on top for
that specific `AppId` without this design needing to mandate it
everywhere; that's a future, opt-in hardening this comparison flags
honestly rather than building preemptively, not a new open question (the
*gate itself* is now fully decided).
