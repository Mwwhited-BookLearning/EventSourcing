<#
.SYNOPSIS
    Starts the .NET Aspire local-dev orchestration (EventStore.AppHost) for
    manual/integration testing on Windows.

.DESCRIPTION
    EventStore.AppHost (ADR-062/026) is the preferred local-dev orchestration
    path -- Postgres, DevIdp, the one-shot EventStore.Migrator, both sample
    seeders/simulators, and client-web's own Vite dev server, all wired up
    and dashboard-grouped, with no docker-compose.yml env vars or a
    pre-generated migration bundle to hand-supply (unlike
    scripts/deploy-docker-local.ps1's own docker-compose.yml path).
    Prefers the standalone `aspire` CLI if installed (faster inner loop,
    same dashboard); falls back to plain `dotnet run` against the AppHost
    project, which works with no extra tooling at all.

.PARAMETER Cli
    Force which launcher to use: 'aspire' or 'dotnet'. Auto-detects if
    omitted.
#>
[CmdletBinding()]
param(
    [ValidateSet("aspire", "dotnet")]
    [string]$Cli
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$appHostProject = Join-Path $repoRoot "src/EventStore.AppHost/EventStore.AppHost.csproj"

if (-not $Cli) {
    $Cli = if (Get-Command aspire -ErrorAction SilentlyContinue) { "aspire" } else { "dotnet" }
}

Push-Location $repoRoot
try {
    if ($Cli -eq "aspire") {
        Write-Host "==> aspire run (project: EventStore.AppHost)" -ForegroundColor Cyan
        aspire run --project $appHostProject
    }
    else {
        Write-Host "==> dotnet run --project src/EventStore.AppHost (aspire CLI not found -- install with 'dotnet tool install -g Aspire.Cli' for a faster inner loop)" -ForegroundColor Cyan
        dotnet run --project $appHostProject
    }
    if ($LASTEXITCODE -ne 0) {
        throw "$Cli exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
