# AppHost pins HTTPS port 5001, which Docker Desktop's backend also uses

**Status: resolved 2026-10-09 (warning).**

## What was wrong

`AppHost.cs` pins the Postgres host's HTTPS endpoint to 5001 (`Port("EventStoreHttps", 5001)`). On this machine `com.docker.backend` already listens on 127.0.0.1:5001, so the Postgres host's HTTPS endpoint and the client-web defaults that assume `https://localhost:5001` can hit Docker instead of the host.

## How it was found

`Get-NetTCPConnection -State Listen` while debugging dead AppHost ports during the load run.

## Root cause

A fixed well-known port with no check for collisions; the Aspire DCP proxy and Docker both claim it depending on start order.

## Resolution

Moving the port would break every doc and client default that cites it, so the AppHost's `Port()` helper now probes each pinned port and prints a `WARNING` naming the port, its `Ports:<Key>` override and a `netstat` hint when it is taken. It first threw, but on this machine Docker's backend holds 5001 and the AppHost has always run anyway (DCP binds around it), so throwing broke a working setup; it warns instead. Verified: the probe fires against the real Docker listener on 5001.
