param(
    [string]$Godot = "godot",
    [int]$NetworkPort = 17000,
    [int]$TimeoutSeconds = 50
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$logRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("pain-taxi-multiplayer-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $logRoot | Out-Null

function Start-SmokePeer([string]$Role, [string]$LogName) {
    $stdout = Join-Path $logRoot "$LogName.out.log"
    $stderr = Join-Path $logRoot "$LogName.err.log"
    $arguments = @("--headless", "--path", $root, "--log-file", $stdout, "--network-port=$NetworkPort", "--multiplayer-smoke-role=$Role")
    if ($Role -eq "host") { $arguments += "--host" } else { $arguments += "--join=127.0.0.1" }
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Godot
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = $arguments -join " "

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    New-Item -ItemType File -Path $stderr -Force | Out-Null
    return $process
}

function Wait-SmokePeer([System.Diagnostics.Process]$Process, [string]$Name) {
    if (-not $Process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $Process.Id -Force
        throw "$Name exceeded $TimeoutSeconds seconds"
    }
    $Process.Refresh()
    $exitCode = $Process.ExitCode
    if ($exitCode -ne 0) { throw "$Name exited with code $exitCode" }
}

try {
    $hostProcess = Start-SmokePeer "host" "host"
    Start-Sleep -Seconds 2
    $firstClient = Start-SmokePeer "client" "client-one"
    Wait-SmokePeer $firstClient "first client"
    Start-Sleep -Seconds 2
    $secondClient = Start-SmokePeer "client" "client-two"
    Wait-SmokePeer $secondClient "second client"
    Wait-SmokePeer $hostProcess "host"

    $logs = (Get-ChildItem -LiteralPath $logRoot -File -Filter "*.log" | Get-Content -Raw) -join [Environment]::NewLine
    $required = @("MULTIPLAYER_SMOKE_CLIENT_PASS", "MULTIPLAYER_SMOKE_DISCONNECT_PASS", "MULTIPLAYER_SMOKE_RECONNECT_PASS")
    foreach ($marker in $required) {
        if ($logs -notmatch [regex]::Escape($marker)) { throw "missing smoke marker: $marker" }
    }
    if ($logs -match "MULTIPLAYER_SMOKE_FAIL|Rejected kart input|SCRIPT ERROR|ERROR:") {
        throw "multiplayer smoke logs contain a failure or rejected input warning"
    }
    Write-Host "Multiplayer two-instance smoke test passed. Logs: $logRoot"
}
catch {
    Write-Error $_.Exception.ToString()
    Write-Host "Logs retained at: $logRoot"
    exit 1
}
