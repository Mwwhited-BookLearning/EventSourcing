# Repeated AppHost restarts leave stale containers and dead pinned ports

**Status: open (script helps sometimes; not a reliable fix).**

## What was wrong

After several `aspire stop`/`aspire start` cycles (and killed AppHosts), pinned ports 5000-5011 stopped listening while the AppHost reported "running": multiple orphan `dcp` processes, `Created` Docker containers (`sqlserver-server-*`, `postgres-server-*`) referencing deleted `aspire-session-network-*` networks, and ~10k TIME_WAIT sockets after load.

## Workaround

`aspire stop`, kill `dcp`/`dcpctrl`/`EventStore.AppHost`, `docker rm -f` the stale containers, `docker network rm` the `aspire-session-*` networks, then `aspire start`.

## Mitigation

`scripts/aspire-clean.ps1` performs the workaround above in one step. The underlying Aspire/DCP behaviour is not fixed here; report upstream if it keeps recurring.

Observed later the same day: after `scripts/aspire-clean.ps1` + `aspire start`, the first restart worked, but a later one left `postgres-server` and `sqlserver-server` in `Created` indefinitely (DCP never started them; `docker start` on one succeeded) and the pinned ports 5000-5010 refused connections. So the script is a partial mitigation only. Next step: capture the DCP log (`~/.aspire/logs`) for a failing start to find why DCP stops reconciling, or report upstream.

DCP log from the failing start (`%TEMP%\aspire-dcp*\*_out`): `ContainerReconciler` logged `Added new ContainerNetworkConnection` for both database containers on `aspire-container-network`, then nothing further for them (no start). The stall is after network attach, before container start.

Third clean start the same day (script, then `aspire start`): both containers were `Up`, `aspire describe` showed `devidp` and `eventstore-sqlite` as Running/Healthy, yet no process listened on any pinned port (5000-5011) and no project process (`EventStore.Host.*`, DevIdp) existed; only `EventStore.AppHost` and one `dcp` were alive. Windows excluded port ranges were checked and do not cover 5000-5011. So DCP can report a resource Running when its process is gone. Needs an upstream report with the DCP logs.

Fourth attempt, started with `dotnet run --project src/EventStore.AppHost` instead of `aspire start` (after the cleanup script): all four hosts answered on 5000/5002/5004/5010 within about a minute, so the Aspire CLI's detach mode is not the whole story. About 126 publishes into a load run every project process (DevIdp and all three hosts) vanished at once while the AppHost, `dcp` and both database containers stayed up; the Windows Application log shows `postgres-server_check`/`sqlserver-server_check`/`SqlServer_check` health checks Unhealthy at that moment. That points to Docker Desktop networking dropping, not the repo's code, but is unproven.

## Draft upstream report (not filed)

**Title:** DCP leaves containers in `Created` / reports projects Running with no process after repeated AppHost restarts (Windows, Docker Desktop)

**Environment:** Windows 11, Docker Desktop, Aspire CLI 13.x (13.6.1 available), .NET 10, AppHost with two database containers (Postgres, SQL Server) and several pinned-port projects.

**Observed (same day, repeated `aspire start` / `aspire stop` cycles):**
1. Dead pinned ports: the DCP proxy ports answered nothing while resources showed Running; stale `Created` containers pointed at deleted `aspire-session-*` networks.
2. After manual cleanup (stop, kill `dcp`/AppHost, `docker rm -f` the database containers, `docker network rm aspire-session-*`), one start worked; the next left both containers in `Created` forever. DCP's `ContainerReconciler` logged `Added new ContainerNetworkConnection` for both and nothing after; `docker start` on one succeeded manually.
3. Another clean start: containers `Up`, `aspire describe` showed projects Running/Healthy, but no project process and no listener existed on any pinned port.
4. Starting with `dotnet run --project <AppHost>` brought everything up, but under ~126 requests of load every project process vanished at once while the AppHost, `dcp` and containers stayed up.

**Expected:** a restart either converges to a working state or reports the failure; resources are not reported Running when their process is gone.

**Attach:** `%TEMP%\aspire-dcp*\*_out` from a failing start and `~/.aspire/logs/cli_*.log`.
