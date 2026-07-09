param(
    [string]$Godot = "godot",
    [string]$Preset = "Windows Desktop",
    [string]$Output = "build/windows/kart_racer.exe"
)

$root = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $root $Output
$outputDir = Split-Path -Parent $outputPath

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
dotnet build (Join-Path $root "kart_racer.sln")
& $Godot --headless --path $root --export-release $Preset $outputPath
