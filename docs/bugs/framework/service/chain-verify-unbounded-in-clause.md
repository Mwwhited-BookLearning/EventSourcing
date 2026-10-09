# `/events/verify` returns 500 on a large log (SQLite)

## What was wrong

`GET /events/verify` returned HTTP 500 on the SQLite host after the load runs had grown the log past tens of thousands of events.

## How it was found

The load driver's verify check after a 3000-publish burst, on a database holding earlier runs.

## Root cause

`ChainVerificationService` loaded the whole log, then ran one `ChildEventId IN (every event id)` query. That exceeds SQLite's bound-parameter limit, and also holds the entire log in memory on every provider.

## Resolution

Verification now pages by `SequenceNumber` (1000 per page), looking up parent links per page and carrying the running chain hash across pages. Behaviour and result shape are unchanged. Re-run: `verify: clean` on all three hosts.
