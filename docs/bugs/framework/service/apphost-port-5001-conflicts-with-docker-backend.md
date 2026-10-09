# AppHost pins HTTPS port 5001, which Docker Desktop's backend also uses

**Status: open.**

## What was wrong

`AppHost.cs` pins the Postgres host's HTTPS endpoint to 5001 (`Port("EventStoreHttps", 5001)`). On this machine `com.docker.backend` already listens on 127.0.0.1:5001, so the Postgres host's HTTPS endpoint and the client-web defaults that assume `https://localhost:5001` can hit Docker instead of the host.

## How it was found

`Get-NetTCPConnection -State Listen` while debugging dead AppHost ports during the load run.

## Root cause

A fixed well-known port with no check for collisions; the Aspire DCP proxy and Docker both claim it depending on start order.

## Next step

Decide whether to move the pinned port (and the docs/client defaults that cite it) or fail fast in the AppHost when it is taken.
