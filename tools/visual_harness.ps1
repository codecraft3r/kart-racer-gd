<##
.SYNOPSIS
    Compatibility entry point for the supervised PAIN TAXI visual harness.

The process supervisor, build receipt, result validation, run directory allocation,
logs, manifest, and HTML report all live in PainTaxiHarness.psm1. This wrapper keeps
the historical camera and capture options available to existing callers.
##>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$State = 'menu',
    [Parameter(Position = 1)][string]$Output,
    [string]$Resolution = '1920x1080',
    [string]$Suite,
    [string]$Scene,
    [string]$CameraPreset = 'default',
    [string]$CameraPos,
    [string]$CameraTarget,
    [double]$CameraFov = 0,
    [int]$VehicleIndex = -1,
    [int]$PixelSize = -1,
    [Nullable[bool]]$Crt,
    [Nullable[bool]]$Scanlines,
    [int]$Seed = 1337,
    [int]$BurstCount = 1,
    [double]$BurstInterval = 0.2,
    [double]$WaitSeconds = -1,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120,
    [ValidateSet('', 'fixture', 'journey')][string]$SetupMode = '',
    [switch]$SkipBuild,
    [switch]$NoMetadata,
    [switch]$NoContactSheet,
    [switch]$Visible,
    [switch]$Open,
    [string]$GodotPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'harness/PainTaxiHarness.psm1') -Force

if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $GodotPath = Resolve-HarnessGodotExecutable
}

$manifest = Invoke-PainTaxiHarness `
    -ProjectRoot $ProjectRoot `
    -GodotPath $GodotPath `
    -State $State `
    -Suite $Suite `
    -Resolution $Resolution `
    -Seed $Seed `
    -TimeoutSeconds $TimeoutSeconds `
    -Output $Output `
    -SetupMode $SetupMode `
    -SkipBuild:$SkipBuild `
    -Scene $Scene `
    -CameraPreset $CameraPreset `
    -CameraPos $CameraPos `
    -CameraTarget $CameraTarget `
    -CameraFov $CameraFov `
    -VehicleIndex $VehicleIndex `
    -PixelSize $PixelSize `
    -Crt $Crt `
    -Scanlines $Scanlines `
    -BurstCount $BurstCount `
    -BurstInterval $BurstInterval `
    -WaitSeconds $WaitSeconds `
    -Visible:$Visible `
    -NoContactSheet:$NoContactSheet `
    -NoMetadata:$NoMetadata

Write-Host "Harness manifest: $($manifest.root)\manifest.json" -ForegroundColor Green
Write-Host "Harness report:   $($manifest.root)\report.html" -ForegroundColor Cyan
if ($Open) {
    Start-Process (Join-Path $manifest.root 'report.html')
}
