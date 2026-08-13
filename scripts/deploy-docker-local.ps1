<#
.SYNOPSIS
    Stands up docker-compose.yml locally for manual/integration testing on
    Windows, generating whatever it needs first.

.DESCRIPTION
    docker-compose.yml (ADR-026) is the real PRODUCTION deployment path, not
    a dev convenience file -- deliberately, it has no default
    POSTGRES_PASSWORD/EVENTSTORE_JWT_AUTHORITY and expects a migration
    bundle to already exist at scripts/bundles/postgres/efbundle (generated
    by scripts/generate-migration-bundle.sh, ADR-076). This script supplies
    both, but ONLY here, as a LOCAL TESTING convenience -- it does not
    change docker-compose.yml's own production posture at all.

    scripts/generate-migration-bundle.sh is a bash script (needs `dotnet
    ef`, self-contained linux-x64 output) -- this wrapper shells out to Git
    Bash to run it unchanged rather than reimplementing it in PowerShell,
    the same "don't duplicate an already-correct script" posture as not
    rewriting docker-compose.yml itself.

.PARAMETER PostgresPassword
    Defaults to a fixed, clearly-local-only value if not supplied and
    $env:POSTGRES_PASSWORD isn't already set. Never use this default for
    anything but local testing.

.PARAMETER SkipBundleRegeneration
    Skip regenerating the migration bundle even if one already exists --
    useful once you've already generated it and are just restarting
    containers.

.PARAMETER Down
    Tear the stack down (docker compose down) instead of starting it.
#>
[CmdletBinding()]
param(
    [string]$PostgresPassword,
    [switch]$SkipBundleRegeneration,
    [switch]$Down
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    # Needed even for `down` -- docker compose interpolates every env var the
    # compose file references (including POSTGRES_PASSWORD's own
    # required-value check) before running ANY subcommand, teardown
    # included, not just `up`. Found by running -Down for real: it failed
    # with the exact same "POSTGRES_PASSWORD must be set" error `up` would
    # give, even though tearing down needs no real password at all.
    if (-not $env:POSTGRES_PASSWORD) {
        $env:POSTGRES_PASSWORD = if ($PostgresPassword) { $PostgresPassword } else { "local-testing-only-password" }
        Write-Host "==> POSTGRES_PASSWORD not set -- using a local-testing-only default (never use this for anything real)" -ForegroundColor Yellow
    }

    if ($Down) {
        Write-Host "==> docker compose down" -ForegroundColor Cyan
        docker compose down
        if ($LASTEXITCODE -ne 0) { throw "docker compose down failed (exit code $LASTEXITCODE)" }
        return
    }

    $bundlePath = Join-Path $repoRoot "scripts/bundles/postgres/efbundle"
    if ((Test-Path $bundlePath) -and $SkipBundleRegeneration) {
        Write-Host "==> Reusing existing migration bundle at $bundlePath (-SkipBundleRegeneration)" -ForegroundColor Yellow
    }
    else {
        # Deliberately not just `Get-Command bash` -- on a stock Windows 10/11
        # install, `bash.exe` on PATH resolves to the OS's own WSL-forwarding
        # shim (System32/WindowsApps), which fails outright with no WSL
        # distro installed. Git for Windows' OWN bash.exe (the one that
        # actually runs this script's POSIX shell content) lives under its
        # install root instead -- found directly, not assumed at one fixed
        # path, since Git for Windows can be installed anywhere.
        $bashCandidates = @()
        $gitCommand = Get-Command git -ErrorAction SilentlyContinue
        if ($gitCommand) {
            # git.exe is typically <root>\cmd\git.exe or <root>\bin\git.exe -- bash.exe lives at <root>\bin\bash.exe either way.
            $gitRoot = Split-Path -Parent (Split-Path -Parent $gitCommand.Source)
            $bashCandidates += (Join-Path $gitRoot "bin\bash.exe")
        }
        $bashCandidates += "C:\Program Files\Git\bin\bash.exe"
        $bashCandidates += "C:\Program Files (x86)\Git\bin\bash.exe"
        $bashExe = $bashCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $bashExe) {
            throw "Git for Windows' own bash.exe was not found (checked: $($bashCandidates -join ', ')) -- it's required to run scripts/generate-migration-bundle.sh"
        }
        Write-Host "==> Generating the Postgres migration bundle (scripts/generate-migration-bundle.sh via $bashExe)" -ForegroundColor Cyan
        & $bashExe "scripts/generate-migration-bundle.sh" "postgres"
        if ($LASTEXITCODE -ne 0) { throw "generate-migration-bundle.sh failed (exit code $LASTEXITCODE)" }
    }

    Write-Host "==> docker compose up --build -d" -ForegroundColor Cyan
    docker compose up --build -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed (exit code $LASTEXITCODE)" }

    Write-Host "`nStack is up. eventstore is published at http://localhost:8080." -ForegroundColor Green
    Write-Host "Tail logs:   docker compose logs -f"
    Write-Host "Tear down:   ./scripts/deploy-docker-local.ps1 -Down"
}
finally {
    Pop-Location
}
