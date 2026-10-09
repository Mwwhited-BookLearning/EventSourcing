# AppHost pins HTTPS port 5001, which Docker Desktop's backend also uses

**Status: resolved 2026-10-09 (fail-fast).**

## What was wrong

`AppHost.cs` pins the Postgres host's HTTPS endpoint to 5001 (`Port("EventStoreHttps", 5001)`). On this machine `com.docker.backend` already listens on 127.0.0.1:5001, so the Postgres host's HTTPS endpoint and the client-web defaults that assume `https://localhost:5001` can hit Docker instead of the host.

## How it was found

`Get-NetTCPConnection -State Listen` while debugging dead AppHost ports during the load run.

## Root cause

A fixed well-known port with no check for collisions; the Aspire DCP proxy and Docker both claim it depending on start order.

## Resolution

Moving the port would break every doc and client default that cites it, so the AppHost's `Port()` helper now probes each pinned port before use and throws with the port, its `Ports:<Key>` override name and a `netstat` hint when it is taken. Not verified against a live collision (the AppHost was not restarted this pass).
