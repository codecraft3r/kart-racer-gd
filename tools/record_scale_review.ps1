param(
    [string]$Output = "captures/road_scale_review.avi",
    [string]$Resolution = "854x480",
    [int]$Fps = 30
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$KnownGodot = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64_console.exe"
$GodotCommand = Get-Command "godot" -ErrorAction SilentlyContinue

if ($GodotCommand) {
    $Godot = $GodotCommand.Source
} elseif (Test-Path -LiteralPath $KnownGodot) {
    $Godot = $KnownGodot
} else {
    throw "Godot executable not found. Install Godot or add it to PATH."
}

if ([System.IO.Path]::IsPathRooted($Output)) {
    $OutputPath = $Output
} else {
    $OutputPath = Join-Path $ProjectRoot $Output
}

$OutputDir = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Push-Location $ProjectRoot
try {
    & $Godot `
        --path $ProjectRoot `
        --resolution $Resolution `
        --fixed-fps $Fps `
        --disable-vsync `
        --write-movie $OutputPath `
        --script res://tests/scale_capture_driver.gd
} finally {
    Pop-Location
}
