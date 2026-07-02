#Requires -Version 7
<#
.SYNOPSIS
Single entry point: builds everything, verifies correctness, runs the benchmarks.

.DESCRIPTION
Pipeline: cargo build (cdylib + csbindgen codegen) -> uniffi-bindgen-cs codegen
(stock + span flavors from the local fork) -> dotnet build -> smoke verification
(two processes, see Smoke.cs) -> BenchmarkDotNet run -> results + environment
provenance under .\results.

.EXAMPLE
.\run_benchmarks.ps1                        # full suite (slow, publication-quality)
.\run_benchmarks.ps1 -Quick                 # short BDN job for iteration
.\run_benchmarks.ps1 -Filter *Case04*       # one case
.\run_benchmarks.ps1 -SmokeOnly             # correctness check only
#>
[CmdletBinding()]
param(
    # BenchmarkDotNet glob filter(s), e.g. *Case04* or '*Case05*','*Case07*'.
    [string[]]$Filter = @("*"),
    # Use BDN's short job: fewer warmups/iterations. For iteration, not publication.
    [switch]$Quick,
    # Stop after the smoke verification.
    [switch]$SmokeOnly,
    # Skip uniffi-bindgen-cs codegen (reuse the committed Generated/ bindings).
    [switch]$SkipCodegen,
    # Path to the uniffi-bindgen-cs fork checkout (span_feature branch).
    [string]$BindgenRepo = (Join-Path $PSScriptRoot "..\uniffi-bindgen-cs"),
    # Extra args passed to BenchmarkDotNet verbatim, e.g. @('--runtimes','net8.0').
    [string[]]$BdnArgs = @()
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$rustDir = Join-Path $root 'rust'
$projDir = Join-Path $root 'dotnet\FfiBench'
$resultsDir = Join-Path $root 'results'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        throw "step failed ($LASTEXITCODE): $Name"
    }
}

Invoke-Step "cargo build --release (benchffi cdylib + csbindgen codegen)" {
    cargo build --release --manifest-path (Join-Path $rustDir 'Cargo.toml')
}

if (-not $SkipCodegen) {
    $dll = Join-Path $rustDir 'target\release\benchffi.dll'
    $bindgenManifest = Join-Path $BindgenRepo 'bindgen\Cargo.toml'
    if (-not (Test-Path $bindgenManifest)) {
        throw "uniffi-bindgen-cs fork not found at '$BindgenRepo' (pass -BindgenRepo)"
    }
    Push-Location $rustDir
    try {
        Invoke-Step "uniffi-bindgen-cs codegen (stock flavor)" {
            cargo run --quiet --manifest-path $bindgenManifest -- `
                --library $dll --out-dir ..\dotnet\FfiBench\Generated\stock `
                --config uniffi-stock.toml --no-format
        }
        Invoke-Step "uniffi-bindgen-cs codegen (span flavor, high_performance_strings)" {
            cargo run --quiet --manifest-path $bindgenManifest -- `
                --library $dll --out-dir ..\dotnet\FfiBench\Generated\span `
                --config uniffi-span.toml --no-format
        }
    }
    finally {
        Pop-Location
    }
}

Invoke-Step "dotnet build -c Release" {
    dotnet build (Join-Path $projDir 'FfiBench.csproj') -c Release --nologo -v q
}

$benchDll = Join-Path $projDir 'bin\Release\net8.0\FfiBench.dll'
Invoke-Step "smoke: csbindgen + stock uniffi" { dotnet $benchDll --smoke }
Invoke-Step "smoke: span-flavor uniffi" { dotnet $benchDll --smoke-span }

if ($SmokeOnly) {
    Write-Host "smoke passed; -SmokeOnly set, stopping here." -ForegroundColor Green
    exit 0
}

New-Item -ItemType Directory -Force $resultsDir | Out-Null

# Environment provenance (uniffi_migration_benchmarks.md: record fork commit hash,
# upstream version, toolchains).
$forkRev = git -C $BindgenRepo rev-parse HEAD
$forkBranch = git -C $BindgenRepo rev-parse --abbrev-ref HEAD
$benchRev = git -C $root rev-parse HEAD 2>$null
@"
timestamp            : $(Get-Date -Format o)
bench repo rev       : $benchRev
uniffi-bindgen-cs    : $forkBranch @ $forkRev
uniffi-bindgen-cs ver: $(Select-String -Path (Join-Path $BindgenRepo 'bindgen\Cargo.toml') -Pattern '^version' | Select-Object -First 1 | ForEach-Object { $_.Line })
rustc                : $(rustc --version)
dotnet SDK           : $(dotnet --version)
os                   : $([System.Environment]::OSVersion.VersionString), $(if ([System.Environment]::Is64BitOperatingSystem) { [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture })
filter               : $Filter
quick                : $Quick
"@ | Set-Content (Join-Path $resultsDir 'environment.txt')

$bdnArgList = @('--filter') + $Filter + @('--artifacts', $resultsDir, '--stopOnFirstError')
if ($Quick) {
    $bdnArgList += @('--job', 'short')
}
$bdnArgList += $BdnArgs

# BDN's out-of-process toolchain resolves FfiBench.csproj relative to the current
# directory, so the run must start from the project directory.
Push-Location $projDir
try {
    Invoke-Step "BenchmarkDotNet run (filter: $Filter$(if ($Quick) { ', short job' }))" {
        dotnet $benchDll @bdnArgList
    }
}
finally {
    Pop-Location
}

Write-Host "`nDone. Reports: $resultsDir\results\*-report-github.md, environment: $resultsDir\environment.txt" -ForegroundColor Green
