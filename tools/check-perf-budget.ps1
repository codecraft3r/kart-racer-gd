[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [string]$SummaryPath,
    [string]$BudgetPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ProjectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($BudgetPath)) {
    $BudgetPath = Join-Path $ProjectRoot "perf/budgets.json"
}

if (-not (Test-Path -LiteralPath $BudgetPath)) {
    throw "Perf budget file not found: $BudgetPath"
}

# A directory resolves to its newest summary so callers can point at the output folder.
if (Test-Path -LiteralPath $SummaryPath -PathType Container) {
    $latest = Get-ChildItem -LiteralPath $SummaryPath -Filter "*-summary.json" |
        Sort-Object -Property LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $latest) {
        throw "No *-summary.json found under $SummaryPath"
    }

    $SummaryPath = $latest.FullName
}

if (-not (Test-Path -LiteralPath $SummaryPath)) {
    throw "Perf summary not found: $SummaryPath"
}

$summary = Get-Content -LiteralPath $SummaryPath -Raw | ConvertFrom-Json
$budgets = Get-Content -LiteralPath $BudgetPath -Raw | ConvertFrom-Json

$scenario = [string]$summary.scenario
if ([string]::IsNullOrWhiteSpace($scenario)) {
    throw "Summary $SummaryPath has no scenario field."
}

if ($null -eq $summary.metrics -or $null -eq $summary.metrics.node_count) {
    throw "Summary $SummaryPath has no metrics.node_count to compare."
}

$observed = [double]$summary.metrics.node_count.p99
$entry = $budgets.scenarios.PSObject.Properties[$scenario]

if ($null -eq $entry) {
    Write-Host "Perf budget: no entry for scenario $scenario, nothing to check."
    exit 0
}

$limit = [double]$entry.Value.node_count_max
$usedPercent = ($observed / $limit) * 100.0

Write-Host ("Perf budget [{0}]: node_count p99 {1:N0} against a limit of {2:N0} ({3:N1}% of budget)" -f $scenario, $observed, $limit, $usedPercent)

if ($observed -gt $limit) {
    Write-Host ("FAIL: node_count p99 {0:N0} exceeds the {1:N0} budget for {2}. The scene graph was re-inflated; check recent mesh or instancing work." -f $observed, $limit, $scenario)
    exit 1
}

Write-Host "Perf budget OK."
exit 0
