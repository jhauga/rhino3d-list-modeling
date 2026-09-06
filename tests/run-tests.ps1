<#
.SYNOPSIS
    Runs the List Modeling test suites.

.DESCRIPTION
    Four suites, in the order they should fail in:

      unit        xUnit over the syntax layer, no Rhino needed
      smoke       the plugin loads, registers and does its headline actions
      regression  fixture geometry against hand worked numbers and golden files
      ui          the panel driven by control id, click and keystroke

    The unit suite runs with dotnet test. The other three run inside Rhino,
    which is launched with a script that writes tests/artifacts/rhino-report.json.
    Rhino's own exit code says nothing about the results, so this script reads
    that report.

.PARAMETER Suite
    all (default), unit, rhino, smoke, regression, or ui. Comma separated values
    are accepted, for example "unit,ui".

.PARAMETER UpdateGolden
    Rewrites the golden baselines instead of comparing against them. Review the
    resulting diff before committing.

.PARAMETER SkipBuild
    Skips rebuilding the plugin before the in-Rhino suites.

.PARAMETER Template
    The .3dm Rhino opens. The unit conversion checks assume an inch document.

.EXAMPLE
    powershell -File tests\run-tests.ps1
    powershell -File tests\run-tests.ps1 -Suite unit
    powershell -File tests\run-tests.ps1 -Suite ui -SkipBuild
#>
[CmdletBinding()]
param(
    [string] $Suite = "all",
    [switch] $UpdateGolden,
    [switch] $SkipBuild,
    [string] $Template = "$env:APPDATA\McNeel\Rhinoceros\8.0\Localization\en-US\Template Files\Large Objects - Inches.3dm",
    [string] $RhinoExe = "C:\Program Files\Rhino 8\System\Rhino.exe",
    [int]    $TimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"

$testsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $testsDir
$artifacts = Join-Path $testsDir "artifacts"
$report = Join-Path $artifacts "rhino-report.json"
$log = Join-Path $artifacts "rhino-tests.log"
$project = Join-Path $root "src\ListModeling\ListModeling.csproj"
$unitProject = Join-Path $testsDir "ListModeling.Syntax.Tests\ListModeling.Syntax.Tests.csproj"
$binDir = Join-Path $root "src\ListModeling\bin\Release\net8.0"

$requested = @()
foreach ($name in $Suite.Split(",")) { $requested += $name.Trim().ToLower() }

function Wants([string] $name) {
    if ($requested -contains "all") { return $true }
    if ($requested -contains $name) { return $true }
    if ($requested -contains "rhino") {
        if ($name -eq "smoke" -or $name -eq "regression" -or $name -eq "ui") { return $true }
    }
    return $false
}

if (-not (Test-Path $artifacts)) { New-Item -ItemType Directory -Path $artifacts | Out-Null }
if ($UpdateGolden) { $env:LISTMODELING_UPDATE_GOLDEN = "1" } else { $env:LISTMODELING_UPDATE_GOLDEN = "0" }

$unitOk = $true
$rhinoOk = $true
$ranAnything = $false

# ---- unit -------------------------------------------------------------------

if (Wants "unit") {
    $ranAnything = $true
    Write-Host ""
    Write-Host "== unit suite (dotnet test) ==" -ForegroundColor Cyan
    dotnet test $unitProject --nologo -v q
    if ($LASTEXITCODE -ne 0) { $unitOk = $false }
}

# ---- in-Rhino suites --------------------------------------------------------

$rhinoSuites = @()
foreach ($name in @("smoke", "regression", "ui")) {
    if (Wants $name) { $rhinoSuites += $name }
}

if ($rhinoSuites.Count -gt 0) {
    $ranAnything = $true

    if (-not (Test-Path $RhinoExe)) {
        Write-Host "Rhino was not found at $RhinoExe" -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path $Template)) {
        Write-Host "Template document not found: $Template" -ForegroundColor Red
        Write-Host "Pass -Template with an inch template; the unit checks assume inches." -ForegroundColor Red
        exit 1
    }

    if (-not $SkipBuild) {
        Write-Host ""
        Write-Host "== building the plugin (Release) ==" -ForegroundColor Cyan
        dotnet build $project -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            Write-Host "The plugin did not build; the in-Rhino suites cannot run." -ForegroundColor Red
            exit 1
        }
    }

    Write-Host ""
    Write-Host ("== in-Rhino suites: " + ($rhinoSuites -join ", ") + " ==") -ForegroundColor Cyan

    if (Test-Path $report) { Remove-Item $report -Force }
    if (Test-Path $log) { Clear-Content $log }

    # RHINO_PACKAGE_DIRS makes Rhino load the freshly built plugin with no install step.
    $env:RHINO_PACKAGE_DIRS = $binDir + "\"
    $env:LISTMODELING_TESTS_DIR = $testsDir
    $env:LISTMODELING_RHP = Join-Path $binDir "ListModeling.rhp"
    $env:LISTMODELING_SUITES = ($rhinoSuites -join ",")

    $entry = Join-Path $testsDir "rhino\run_in_rhino.py"
    $rhinoArgs = "`"$Template`" /nosplash /netcore /runscript=`"-_RunPythonScript $entry`""
    $process = Start-Process -FilePath $RhinoExe -ArgumentList $rhinoArgs -PassThru
    Write-Host ("Rhino started (pid " + $process.Id + "); waiting up to $TimeoutSeconds s")

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if (Test-Path $report) { Start-Sleep -Seconds 4; break }
        $process.Refresh()
        if ($process.HasExited) { break }
    }

    $process.Refresh()
    if (-not $process.HasExited) {
        Write-Host "Rhino did not close on its own; stopping it." -ForegroundColor Yellow
        Stop-Process -Id $process.Id -Force -Confirm:$false
    }

    if (Test-Path $log) { Get-Content $log }

    if (-not (Test-Path $report)) {
        Write-Host ""
        Write-Host "No report was written. Rhino never reached the end of the suites." -ForegroundColor Red
        $rhinoOk = $false
    }
    else {
        $data = Get-Content $report -Raw | ConvertFrom-Json
        Write-Host ""
        Write-Host ("in-Rhino: " + $data.counts.passed + " passed, " +
                    $data.counts.failed + " failed, " + $data.counts.error + " errored")
        if (-not $data.ok) { $rhinoOk = $false }
        if ($data.artifacts) {
            Write-Host "artifacts:"
            foreach ($item in $data.artifacts) {
                Write-Host ("  tests/artifacts/" + $item.path + "  " + $item.description)
            }
        }
    }
}

# ---- summary ----------------------------------------------------------------

Write-Host ""
if (-not $ranAnything) {
    Write-Host "No suite matched -Suite '$Suite'. Use all, unit, rhino, smoke, regression or ui." -ForegroundColor Yellow
    exit 1
}
if ($unitOk -and $rhinoOk) {
    Write-Host "All requested suites passed." -ForegroundColor Green
    exit 0
}
Write-Host "Some suites failed." -ForegroundColor Red
exit 1
