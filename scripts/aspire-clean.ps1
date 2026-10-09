# Clears stale Aspire/DCP state left behind by repeated AppHost restarts
# (docs/bugs/framework/service/aspire-restart-leaves-stale-containers-and-dead-ports.md):
# orphan dcp processes, Created/stale database containers, and deleted-network leftovers.
# Run before `aspire start` when the pinned ports (5000-5011) answer nothing.
$ErrorActionPreference = 'Continue'
aspire stop 2>&1 | Out-Null
Get-Process dcp, dcpctrl, EventStore.AppHost -ErrorAction SilentlyContinue | Stop-Process -Force
docker ps -a --format '{{.Names}}' |
    Where-Object { $_ -match '^(sqlserver|postgres)-server' } |
    ForEach-Object { docker rm -f $_ | Out-Null; "removed container $_" }
docker network ls --format '{{.Name}}' |
    Where-Object { $_ -like 'aspire-session-*' } |
    ForEach-Object { docker network rm $_ | Out-Null; "removed network $_" }
"clean; run: aspire start --apphost src/EventStore.AppHost"
