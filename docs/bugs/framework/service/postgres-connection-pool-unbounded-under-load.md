# Postgres hosts have no connection pool cap sized against the server's max_connections

**Status: open (hardening; the failure itself was mitigated).**

## What was wrong

The load run exhausted Postgres `max_connections` (`too many clients`). The immediate fix raised the AppHost container to `max_connections=300` and made the wake signal non-throwing, but each host's Npgsql pool still has the default maximum (100) plus one dedicated LISTEN connection per topic, and several hosts share one server. Nothing ties the pool size to the server limit, so a bigger deployment can hit it again.

## Next step

Set `Maximum Pool Size` explicitly in the connection strings and document the budget (hosts x pool + LISTEN connections < max_connections).
