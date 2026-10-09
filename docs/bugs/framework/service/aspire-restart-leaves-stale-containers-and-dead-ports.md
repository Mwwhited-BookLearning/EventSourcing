# Repeated AppHost restarts leave stale containers and dead pinned ports

**Status: mitigated 2026-10-09 (cleanup script; upstream cause not fixed).**

## What was wrong

After several `aspire stop`/`aspire start` cycles (and killed AppHosts), pinned ports 5000-5011 stopped listening while the AppHost reported "running": multiple orphan `dcp` processes, `Created` Docker containers (`sqlserver-server-*`, `postgres-server-*`) referencing deleted `aspire-session-network-*` networks, and ~10k TIME_WAIT sockets after load.

## Workaround

`aspire stop`, kill `dcp`/`dcpctrl`/`EventStore.AppHost`, `docker rm -f` the stale containers, `docker network rm` the `aspire-session-*` networks, then `aspire start`.

## Mitigation

`scripts/aspire-clean.ps1` performs the workaround above in one step. The underlying Aspire/DCP behaviour is not fixed here; report upstream if it keeps recurring.
