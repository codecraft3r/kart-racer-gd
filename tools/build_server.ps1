param(
    [string]$Godot = "godot",
    [string]$Preset = "Linux/X11",
    [string]$Output = "build/server/kart_racer_server.x86_64"
)

$root = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $root $Output
$outputDir = Split-Path -Parent $outputPath

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
dotnet build (Join-Path $root "kart_racer.sln")
& $Godot --headless --path $root --export-release $Preset $outputPath
