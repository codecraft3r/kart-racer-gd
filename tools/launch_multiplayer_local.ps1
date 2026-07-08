param(
    [string]$Godot = "godot",
    [string]$JoinAddress = "127.0.0.1"
)

$root = Split-Path -Parent $PSScriptRoot

Start-Process -FilePath $Godot -ArgumentList @("--path", $root, "--host") -WorkingDirectory $root
Start-Sleep -Seconds 2
Start-Process -FilePath $Godot -ArgumentList @("--path", $root, "--join=$JoinAddress") -WorkingDirectory $root
