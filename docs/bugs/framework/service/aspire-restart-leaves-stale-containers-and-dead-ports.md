# Repeated AppHost restarts leave stale containers and dead pinned ports

**Status: open (dev tooling; workaround known).**

## What was wrong

After several `aspire stop`/`aspire start` cycles (and killed AppHosts), pinned ports 5000-5011 stopped listening while the AppHost reported "running": multiple orphan `dcp` processes, `Created` Docker containers (`sqlserver-server-*`, `postgres-server-*`) referencing deleted `aspire-session-network-*` networks, and ~10k TIME_WAIT sockets after load.

## Workaround

`aspire stop`, kill `dcp`/`dcpctrl`/`EventStore.AppHost`, `docker rm -f` the stale containers, `docker network rm` the `aspire-session-*` networks, then `aspire start`.

## Next step

Script the cleanup (or report upstream to Aspire) and note it in the load-test usage.
