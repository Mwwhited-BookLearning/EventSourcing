# Feature: Outbound Webhooks

Context: decision record `ADR-060` in `../07-adrs.md` (`WebhookSubscription`,
the durable `WebhookOutbox`/`WebhookDeliveryCursor` primitive, Standard
Webhooks-shaped signing) — see [the Standard Webhooks
specification](https://github.com/standard-webhooks/standard-webhooks/blob/main/spec/standard-webhooks.md)
itself for the header/signing convention this ADR adopts rather than
reinvents; folded in below as its own section, `ADR-093` (the
current+previous signing-secret pair and dual-signature emission during a
rotation window). Data model in
[`../data/schema-registry.md`](../data/schema-registry.md) — `WebhookSubscription`
("Webhook subscriptions" section) and `WebhookOutbox`/`WebhookDeliveryCursor`
("Webhook outbox and delivery cursor" section) — this doc shows only the
columns its own scenarios touch; full column lists live there.
`WebhookOutboxPump` is the leader-elected background worker role named in
`../data/schema-registry.md`'s `LeaderLease` (`ADR-078`) that drains the
outbox; election mechanics themselves are `ADR-078`'s concern, not
re-derived here.

This doc deliberately does **not** re-derive:
- **The `{value}`/`{masked}`/`{erased}` masking wrapper mechanics and
  `IPayloadMasker` itself** (`ADR-009`, `ADR-057`) — see
  [`masking.md`](masking.md). This doc only shows *when* a webhook payload
  is masked (at outbox-enqueue time, against the subscription's own frozen
  claim snapshot) and states the one honest limitation that follows,
  never the transform's own recursion/strategy mechanics.
- **`RequiredClaims`, event-type security, and ordinary Bearer-token
  auth/scopes** (`ADR-006`, `ADR-008`/`ADR-050`) — see
  [`event-security.md`](event-security.md) and [`auth.md`](auth.md).
  Registering/rotating a subscription is shown gated behind an
  illustrative `webhooks:admin` scope, following this design's existing
  scope-naming convention (`registry:admin`, `events:publish`) — `03-api-
  contracts.md` doesn't yet enumerate a webhook-management endpoint at
  all (a propagation gap, not exercised further here), so the exact
  scope name is this doc's own reasonable extrapolation, not a cited
  ADR fact.
- **The general durable outbox/inbox primitive's fault/abend/restart-
  tolerance argument itself** (`ADR-023`'s Inbox, `ADR-033`'s peer-sync
  outbox, `ADR-039`'s client outbox) — no core-engine feature doc owns a
  dedicated write-up of that shared primitive yet, so this doc cites
  `ADR-060`'s own "confirms this really does inherit the primitive"
  reasoning inline rather than inventing a summary of it.
- **The publish/ingestion pipeline and `SchemaStatus`/`AuthorityStatus`
  advisory flags** (`ADR-023`, `ADR-035`) — see
  [`publish-event.md`](publish-event.md) and
  [`non-authoritative-capture.md`](non-authoritative-capture.md). This
  doc picks up *after* an event is already durably stored and folded;
  it doesn't re-show how it got there.
- **`ADR-057`'s crypto-shredding erasure mechanism itself** — see
  `../data/event-log.md` and `event-security.md`'s masking section. This
  doc only shows the *consequence* for an already-enqueued or already-
  delivered webhook payload, per `ADR-060`'s own stated honest
  limitation, never how erasure itself is triggered.

## Sequence diagram — subscription registration (fixed claim snapshot)

![Sequence diagram — subscription registration (fixed claim snapshot)](../diagrams/features/webhooks/01-sequence-diagram-subscription-registration-fixed-c.svg)

```plantuml
@startuml Webhooks_Registration_Sequence
autonumber
actor "Ops / App Admin" as admin
participant "Webhook Subscription Endpoint" as endpoint
participant "Auth\n(webhooks:admin scope, illustrative)" as auth
database "Event & Schema Store" as db

admin -> endpoint: POST /webhooks/subscriptions\nBearer <JWT>\n{ appId, targetUrl, eventTypes: [...], signingSecret? }
endpoint -> auth: validate webhooks:admin scope
alt missing scope
  auth --> admin: 403
else scope present
  endpoint -> endpoint: generate SigningSecret if omitted;\ncompute FixedClaimsSnapshot from the registering\ncaller's own current claims, ONCE, now (ADR-060 --\nsame "fixed for the connection's lifetime" rule\nADR-009 already states for a Follow connection)
  endpoint -> db: INSERT WebhookSubscription\n{ SubscriptionId, AppId, TargetUrl, SigningSecret,\n  PreviousSigningSecret: null, EventTypes,\n  FixedClaimsSnapshot, Active: true, RegisteredAt }
  endpoint -> db: INSERT WebhookDeliveryCursor\n{ SubscriptionId, LastDeliveredSequenceNumber: 0 }
  endpoint --> admin: 201 { subscriptionId, signingSecret }\n(secret shown once at creation, same as most\nreal webhook providers -- not re-displayed later)
end
@enduml
```

`FixedClaimsSnapshot` is never re-evaluated after this point — if the
registering caller's own claims later change (a role revoked, a new one
granted), this subscription keeps masking against the snapshot taken here,
exactly as `ADR-009` already requires for a live Follow connection.

## Sequence diagram — event delivery, durable outbox, retry/backoff, dead-letter

![Sequence diagram — event delivery, durable outbox, retry/backoff, dead-letter](../diagrams/features/webhooks/02-sequence-diagram-event-delivery-durable-outbox-ret.svg)

```plantuml
@startuml Webhooks_Delivery_Sequence
autonumber
participant "Router\n(ADR-023)" as router
participant "IPayloadMasker\n(see masking.md)" as masker
database "Event Log" as eventLog
database "WebhookOutbox /\nWebhookDeliveryCursor" as outboxDb
participant "WebhookOutboxPump\n(leader-elected, ADR-078)" as pump
participant "Webhook Target\n(external HTTPS endpoint)" as target

router -> outboxDb: does this StoredEvent's EventType/EntityType\nmatch an Active WebhookSubscription.EventTypes\nfor this AppId?
alt no matching Active subscription
  router -> router: nothing enqueued
else one or more matches
  router -> masker: mask(Payload, subscription.FixedClaimsSnapshot)
  masker --> router: masked EventPayloadSnapshot
  router -> outboxDb: INSERT WebhookOutbox\n{ SequenceNumber, SubscriptionId, EventPayloadSnapshot, EnqueuedAt }
  note right of outboxDb
    A durable table, never an in-memory queue --
    nothing queued here is lost to an unclean
    process termination (ADR-060/ADR-033's own
    fault-tolerance bar).
  end note
end
... asynchronously, on WebhookOutboxPump's poll interval ...
loop while WebhookOutbox rows exist past the cursor
  pump -> outboxDb: SELECT WebhookOutbox WHERE SequenceNumber >\n  WebhookDeliveryCursor.LastDeliveredSequenceNumber\n  ORDER BY SequenceNumber
  pump -> pump: build Standard Webhooks headers:\nwebhook-id (doubles as idempotency key),\nwebhook-timestamp,\nwebhook-signature = HMAC-SHA256("{id}.{timestamp}.{payload}", SigningSecret)
  pump -> target: POST TargetUrl\nwebhook-id / webhook-timestamp / webhook-signature\nbody: EventPayloadSnapshot
  alt 2xx response
    target --> pump: 200
    pump -> outboxDb: UPDATE WebhookDeliveryCursor\nSET LastDeliveredSequenceNumber, LastAttemptAt, LastSuccessAt
  else non-2xx / timeout, retries remain
    target --> pump: error or timeout
    pump -> pump: schedule retry after exponential\nbackoff + jitter (Standard Webhooks' own recommendation)
  else retries exhausted
    pump -> eventLog: append reserved "WebhookDeliveryFailed" event\n{ SubscriptionId, TargetSequenceNumber, Attempts, LastError }
    note right of eventLog
      A reserved, platform-level event type, never
      registered via PUT /registry/{event-type} -- the
      same "make the failure an inspectable record"
      posture ADR-020's EventUpcastFailed already
      established. Queryable through the ordinary
      Lineage API, not just operator logs (ADR-060).
    end note
  end
end
@enduml
```

**Honest limitation, stated per `ADR-060`'s own Consequences**: once a
payload has actually left `pump` for `target` with a `2xx` response, this
framework has no further control over that copy. If `ADR-057`'s
crypto-shredding later destroys the key protecting a field that payload
carried, the already-delivered copy is not retroactively reachable. A
*retried* delivery attempted after that erasure — still within this same
retry loop, before the cursor advances past it — correctly re-masks
through `IPayloadMasker` against the now-erased key and carries
`{"erased": true}` for that field; only copies already successfully sent
before the erasure are the real exposure.

## Sequence diagram — signing-secret rotation with dual-signature emission (`ADR-093`)

![Sequence diagram — signing-secret rotation with dual-signature emission (`ADR-093`)](../diagrams/features/webhooks/03-sequence-diagram-signing-secret-rotation-with-dual.svg)

```plantuml
@startuml Webhooks_SecretRotation_Sequence
autonumber
actor "Ops / App Admin" as admin
participant "Webhook Subscription Endpoint" as endpoint
database "WebhookSubscription" as db
participant "WebhookOutboxPump" as pump
participant "Webhook Target" as target

admin -> endpoint: POST /webhooks/subscriptions/{id}/rotate-secret\n{ newSigningSecret? }
endpoint -> db: UPDATE WebhookSubscription\nSET PreviousSigningSecret = SigningSecret,\n    SigningSecret = new (generated if omitted)
note right of db
  A schema-level current+previous PAIR (ADR-093) --
  not a config toggle. Without this second column,
  no operational discipline could rotate without
  either breaking in-flight verification on the old
  key or never rotating at all.
end note
... ops-configured overlap window (rotation cadence itself\nstays deployment policy, ADR-093 -- not shown here) ...
loop each delivery while PreviousSigningSecret is set
  pump -> pump: compute TWO signatures --\none against SigningSecret, one against PreviousSigningSecret\n(Standard Webhooks' own documented multi-signature shape)
  pump -> target: POST ... \nwebhook-signature: v1,<sig-current> v1,<sig-previous>
  target -> target: verifies against whichever secret\nit still has cached locally -- tolerates the rotation\nwithout a synchronized cutover moment
end
admin -> endpoint: POST /webhooks/subscriptions/{id}/discard-previous-secret\n(once ops decides the overlap window has elapsed)
endpoint -> db: UPDATE WebhookSubscription SET PreviousSigningSecret = null
@enduml
```

No new signing mechanism is invented for rotation — `ADR-093` uses exactly
the multiple-simultaneous-secret shape the Standard Webhooks spec already
supports, which `ADR-060` had already adopted more fully than it originally
exercised.

## Data model (ER diagram)

![Data model (ER diagram)](../diagrams/features/webhooks/04-data-model-er-diagram.svg)

```plantuml
@startuml Webhooks_ER
hide circle
skinparam linetype ortho

entity "WebhookSubscription" as sub {
  * SubscriptionId : guid <<PK>>
  --
  AppId : string
  TargetUrl : string
  SigningSecret : string
  PreviousSigningSecret : string?
  ' set only during a rotation overlap window (ADR-093)
  EventTypes : list<string>
  FixedClaimsSnapshot : text
  ' JSON -- computed ONCE at registration (ADR-060)
  Active : bool
  RegisteredAt : datetimeoffset
}

entity "WebhookOutbox" as outbox {
  * SequenceNumber : bigint <<PK>>
  --
  SubscriptionId : guid <<FK>>
  EventPayloadSnapshot : text
  ' masked against FixedClaimsSnapshot at enqueue time
  EnqueuedAt : datetimeoffset
}

entity "WebhookDeliveryCursor" as cursor {
  * SubscriptionId : guid <<PK, FK>>
  --
  LastDeliveredSequenceNumber : bigint
  LastAttemptAt : datetimeoffset
  LastSuccessAt : datetimeoffset?
}

entity "StoredEvent\n(WebhookDeliveryFailed, reserved type)" as failed {
  * SequenceNumber : bigint <<PK>>
  --
  EventType : string
  ' always "WebhookDeliveryFailed" for this row shape
  Payload : text
  ' { SubscriptionId, TargetSequenceNumber, Attempts, LastError }
}

sub ||--o{ outbox : "EventTypes match enqueues here"
sub ||--|| cursor : "one resumption point per subscription"
outbox ..> failed : "exhausted-retry rows are what a\nWebhookDeliveryFailed event reports on\n(logical only, not a DB foreign key)"

note right of cursor
  Structurally identical to PeerSyncCursor
  (../data/schema-registry.md) -- confirms this
  really does inherit ADR-033's durable-checkpoint
  primitive, not merely resemble it (ADR-060).
end note
@enduml
```

Full column lists are in `../data/schema-registry.md` — this diagram shows
only what registration, delivery, and dead-lettering actually read/write.

## Salt (UI mockup) — registering, monitoring, and inspecting a failed delivery

### Screen 1: Register a webhook subscription

![Screen 1: Register a webhook subscription diagram](../diagrams/features/webhooks/05-screen-1-register-a-webhook-subscription.svg)

```plantuml
@startsalt
{
  { "Register Webhook Subscription -- App 'demo'" }
  ..
  { "Target URL" | "^https://ops.example.com/hooks/order-events^" }
  { "Event types" | [X] OrderPlaced [X] OrderShipped [ ] PatientAdmitted }
  { "Signing secret" | ( ) Auto-generate ( ) Provide my own }
  ..
  "Claims snapshot (frozen at save, ADR-060): events:follow, clearance:none"
  ..
  { [ Cancel ] | [ Register Subscription ] }
}
@endsalt
```

### Screen 2: Subscriptions dashboard

![Screen 2: Subscriptions dashboard diagram](../diagrams/features/webhooks/06-screen-2-subscriptions-dashboard.svg)

```plantuml
@startsalt
{
  { "Webhook Subscriptions -- App 'demo'" }
  ..
  | Target                          | Event types           | Active | Last success        | .        |
  | ops.example.com/hooks/order-... | OrderPlaced, Order...  | [X]    | 2026-08-03 09:14 UTC | [ Rotate secret ] [ History ] |
  | billing.example.com/hooks/pay.. | PaymentCaptured        | [ ]    | (never)              | [ Rotate secret ] [ History ] |
}
@endsalt
```

Clicking **Rotate secret** dispatches `ADR-093`'s rotation flow (Screen 1's
sequence diagram above, "signing-secret rotation" diagram); clicking
**History** opens Screen 3 for that subscription.

### Screen 3: Delivery history, including a dead-lettered event

![Screen 3: Delivery history, including a dead-lettered event diagram](../diagrams/features/webhooks/07-screen-3-delivery-history-including-a-dead-lettere.svg)

```plantuml
@startsalt
{
  { "ops.example.com/hooks/order-... -- Delivery History" }
  ..
  | SequenceNumber | Status         | Attempts | Last error                  |
  | 48210          | delivered      | 1        | --                          |
  | 48213          | delivered      | 3        | 503 (retried, then succeeded) |
  | 48219          | WebhookDeliveryFailed | 6 | 404 Not Found (exhausted) |
  ..
  "Row 48219 is a real, queryable event -- see it via the Lineage API,\nnot just this screen (ADR-060)."
}
@endsalt
```

## Gherkin

```gherkin
Feature: Outbound Webhooks
  As a hosting team
  I want to register a webhook subscription and have matching events delivered reliably
  So that a consumer doesn't need to hold an open Follow connection to be notified

  # Every admin request below carries a Bearer token with the webhooks:admin
  # scope (illustrative, see this doc's Context) unless a scenario says
  # otherwise. Delivery itself is a background process, not gated by a
  # caller's own token at all.

  Background:
    Given app "demo" has registered event type "OrderPlaced" version 1
    And app "demo" has registered event type "OrderShipped" version 1

  Scenario: Registering a subscription freezes its claim snapshot once, at registration time
    Given the registering caller currently holds claim "clearance" value "none"
    When I POST to "/webhooks/subscriptions" with body:
      """
      { "appId": "demo", "targetUrl": "https://ops.example.com/hooks/order-events", "eventTypes": ["OrderPlaced"] }
      """
    Then the response status should be 201
    And the created subscription's FixedClaimsSnapshot should record claim "clearance" value "none"
    When the registering caller is later granted claim "clearance" value "phi"
    Then the subscription's FixedClaimsSnapshot should still record only "clearance: none"
    # Never re-evaluated after registration -- the same rule ADR-009
    # already applies to a live Follow connection's claims (ADR-060).

  Scenario: A matching event is masked and enqueued into the durable outbox, not an in-memory queue
    Given a subscription targets event type "OrderPlaced" with FixedClaimsSnapshot holding no "clearance" claim
    And "OrderPlaced" has a "CustomerTaxId" property masked behind requiredClaim "clearance:pii"
    When an "OrderPlaced" event is published with body { "Amount": 150.00, "CustomerTaxId": "123-45-6789" }
    Then a WebhookOutbox row should be enqueued for that subscription
    And its EventPayloadSnapshot should carry "CustomerTaxId" as {"masked": "***"}, not the real value
    # Masked at enqueue time, against the FROZEN snapshot -- never a live
    # re-check against the subscriber's current claims (masking.md).

  Scenario: A non-matching event type is never enqueued for that subscription
    Given a subscription targets only event type "OrderPlaced"
    When an "OrderShipped" event is published
    Then no WebhookOutbox row should be enqueued for that subscription

  Scenario: Delivery signs the payload using the Standard Webhooks header shape
    Given a WebhookOutbox row is pending delivery for a subscription with SigningSecret "whsec_test"
    When WebhookOutboxPump attempts delivery
    Then the outbound request should carry headers "webhook-id", "webhook-timestamp", and "webhook-signature"
    And "webhook-signature" should equal the HMAC-SHA256 of "{webhook-id}.{webhook-timestamp}.{payload}" keyed by "whsec_test"

  Scenario: A failed delivery retries with exponential backoff before exhausting
    Given a WebhookOutbox row is pending delivery
    When the target responds 503 on the first two attempts and 200 on the third
    Then the delivery should eventually be recorded as successful
    And each retry should be spaced further apart than the last (backoff + jitter)
    And WebhookDeliveryCursor.LastSuccessAt should be updated only after the successful third attempt

  Scenario: Exhausted retries dead-letter as a queryable WebhookDeliveryFailed event, not a silent failure
    Given a WebhookOutbox row is pending delivery to a target that always responds 404
    When WebhookOutboxPump exhausts its configured retry attempts
    Then a "WebhookDeliveryFailed" event should be appended to app "demo"'s own Event Log
    And that event should be queryable through the ordinary Lineage API
    # Same "make the failure an inspectable record" posture as
    # EventUpcastFailed (ADR-020) -- never just an operator log line.

  Scenario: An already-delivered payload is not retroactively reachable after a later erasure
    Given a WebhookOutbox row for entity "demo:Order:o-1" was delivered successfully before any erasure request
    When entity "demo:Order:o-1"'s "CustomerTaxId" field is later crypto-shredded (ADR-057)
    Then the copy already sent to the webhook target remains exactly as originally delivered
    # Stated as an honest limitation, not glossed over (ADR-060's own
    # Consequences) -- this framework has no further control once sent.

  Scenario: A retry attempted after erasure correctly carries {"erased": true}
    Given a WebhookOutbox row for entity "demo:Order:o-1" has NOT yet been successfully delivered
    And entity "demo:Order:o-1"'s "CustomerTaxId" field is crypto-shredded (ADR-057) before the next retry
    When WebhookOutboxPump retries delivery
    Then the payload sent on this retry should carry "CustomerTaxId" as {"erased": true}

  Scenario: Rotating a subscription's signing secret emits dual signatures during the overlap window (ADR-093)
    Given a subscription's SigningSecret is "whsec_old"
    When I POST to "/webhooks/subscriptions/{id}/rotate-secret" with a new secret "whsec_new"
    Then the subscription's SigningSecret should become "whsec_new"
    And its PreviousSigningSecret should become "whsec_old"
    When a delivery is attempted while PreviousSigningSecret is still set
    Then the "webhook-signature" header should carry two signatures, one valid against "whsec_new" and one valid against "whsec_old"

  Scenario: Discarding the previous secret ends the overlap window
    Given a subscription's PreviousSigningSecret is "whsec_old", per a prior rotation
    When I POST to "/webhooks/subscriptions/{id}/discard-previous-secret"
    Then the subscription's PreviousSigningSecret should become null
    When a delivery is attempted afterward
    Then the "webhook-signature" header should carry only one signature, valid against the current secret
    # How long the overlap window lasts before this is called is ops
    # policy, not a framework-enforced timer (ADR-093).
```
