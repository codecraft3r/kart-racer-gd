param(
    [string]$Godot = "godot",
    [string]$JoinAddress = "127.0.0.1",
    [int]$NetworkPort = 7000
)

$root = Split-Path -Parent $PSScriptRoot

Start-Process -FilePath $Godot -ArgumentList @("--path", $root, "--network-port=$NetworkPort", "--host") -WorkingDirectory $root
Start-Sleep -Seconds 2
Start-Process -FilePath $Godot -ArgumentList @("--path", $root, "--network-port=$NetworkPort", "--join=$JoinAddress") -WorkingDirectory $root
