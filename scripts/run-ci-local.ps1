<#
.SYNOPSIS
    Windows-native PowerShell equivalent of .github/workflows/ci.yml's own
    two jobs, run locally against this exact repository.

.DESCRIPTION
    ADR-091: GitHub Actions is the CI/CD platform, but this environment has
    no push access to a real GitHub remote with Actions enabled (TODO.md's
    own item on this, direct user request) -- local scripts are the actual
    day-to-day way to run the same checks ci.yml runs, on Windows, without
    needing Actions at all. Mirrors ci.yml's `build-and-test` job (dotnet
    restore/build/test, client-web npm ci/test) and `vulnerability-scan` job
    (dotnet list package --vulnerable, npm audit --omit=dev) step-for-step,
    same commands, same order, same failure conditions -- not a reinvented
    equivalent. Keep this in sync with ci.yml by hand; there is no shared
    source between them (GitHub Actions YAML has no local-execution mode
    that would let this file just BE ci.yml).

.PARAMETER Configuration
    Build configuration, matches ci.yml's own -c Release.

.PARAMETER SkipVulnerabilityScan
    Skip the dotnet/npm vulnerability-scan job -- useful for a fast inner-loop
    check when you already know the dependency graph hasn't changed.

.PARAMETER SkipClientWeb
    Skip the client-web (npm) steps entirely -- useful when only working on
    the .NET side.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipVulnerabilityScan,
    [switch]$SkipClientWeb
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Step {
    param([string]$Name, [scriptblock]$Command)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name (exit code $LASTEXITCODE)"
    }
}

Push-Location $repoRoot
try {
    # --- build-and-test job -------------------------------------------------
    Invoke-Step "dotnet restore" { dotnet restore EventStore.slnx }
    Invoke-Step "dotnet build"   { dotnet build EventStore.slnx --no-restore -c $Configuration }
    Invoke-Step "dotnet test (EventStore.IntegrationTests)" {
        dotnet test tests/EventStore.IntegrationTests/EventStore.IntegrationTests.csproj `
            --no-build -c $Configuration --logger "console;verbosity=normal"
    }

    if (-not $SkipClientWeb) {
        Push-Location (Join-Path $repoRoot "client-web")
        try {
            Invoke-Step "npm ci"   { npm ci }
            Invoke-Step "npm test" { npm test }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "==> Skipping client-web steps (-SkipClientWeb)" -ForegroundColor Yellow
    }

    # --- vulnerability-scan job ---------------------------------------------
    if (-not $SkipVulnerabilityScan) {
        Invoke-Step "dotnet list package --vulnerable" {
            $log = Join-Path $repoRoot "vulnerable-nuget.log"
            dotnet list EventStore.slnx package --vulnerable --include-transitive | Tee-Object -FilePath $log
            $hit = Select-String -Path $log -Pattern "has the following vulnerable packages" -Quiet
            if ($hit) {
                throw "dotnet list package --vulnerable found known-vulnerable NuGet packages -- see $log"
            }
        }

        if (-not $SkipClientWeb) {
            Push-Location (Join-Path $repoRoot "client-web")
            try {
                # --omit=dev: production dependencies only, matching ci.yml's own
                # scope -- client-web's devDependency vulnerability chain
                # (vitest/vite/esbuild) is tracked separately in TODO.md, not
                # this gate's job to enforce.
                Invoke-Step "npm audit (production dependencies)" { npm audit --omit=dev --audit-level=high }
            }
            finally {
                Pop-Location
            }
        }
    }
    else {
        Write-Host "==> Skipping vulnerability-scan job (-SkipVulnerabilityScan)" -ForegroundColor Yellow
    }

    Write-Host "`nAll local CI steps passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
