[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("verify", "build", "test", "capture", "launch")]
    [string]$Command = "verify",
    [string]$GodotPath,
    [string]$State = "menu",
    [string]$Output,
    [string]$Resolution = "1920x1080",
    [string]$Suite,
    [int]$Seed = 1337,
    [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120,
    [ValidateSet("", "fixture", "journey")][string]$SetupMode = "",
    [switch]$SkipBuild,
    [switch]$Headless,
    [switch]$SkipWorldGenerationSmoke
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
# Godot writes warnings to stderr even when it exits successfully.  We inspect
# the collected diagnostics ourselves, so stderr must not become a terminating
# PowerShell native-command error before the exit code is checked.
$PSNativeCommandUseErrorActionPreference = $false
$HarnessModule = Join-Path $PSScriptRoot "harness/PainTaxiHarness.psm1"
Import-Module $HarnessModule -Force

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ExpectedGodotVersion = "4.6.3"
$ExpectedGodotSdk = "Godot.NET.Sdk/4.6.3"
$ExpectedDotnetSdk = "8.0.422"

function Resolve-GodotExecutable {
    param([string]$RequestedPath)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add($RequestedPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:GODOT)) {
        $candidates.Add($env:GODOT)
    }
    foreach ($commandName in @("godot", "godot4")) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue
        if ($null -ne $command) {
            $candidates.Add($command.Source)
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $wingetRoot = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64"
        $candidates.Add((Join-Path $wingetRoot "Godot_v4.6.3-stable_mono_win64_console.exe"))
        $candidates.Add((Join-Path $wingetRoot "Godot_v4.6.3-stable_mono_win64.exe"))
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = $null
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $resolved = (Resolve-Path -LiteralPath $candidate).Path
        } else {
            $command = Get-Command $candidate -ErrorAction SilentlyContinue
            if ($null -ne $command) {
                $resolved = $command.Source
            }
        }
        if ($null -eq $resolved) {
            continue
        }

        $version = (& $resolved --version 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $version -match "(?i)4\.6\.3.*mono") {
            Write-Host "Using Godot $version at $resolved"
            return $resolved
        }
    }

    throw "Godot 4.6.3 Mono was not found. Pass -GodotPath <executable>, set GODOT, or install the Mono build."
}

function Assert-ToolchainConfiguration {
    $projectConfig = Get-Content -Raw (Join-Path $ProjectRoot "project.godot")
    if ($projectConfig -notmatch 'config/features=PackedStringArray\("4\.6", "C#", "GL Compatibility"\)') {
        throw "project.godot must target Godot 4.6 with C# and GL Compatibility."
    }
    if ($projectConfig -notmatch 'toolchain/godot_version="4\.6\.3-stable-mono"') {
        throw "project.godot must pin Godot 4.6.3 stable Mono."
    }

    $projectFile = Get-Content -Raw (Join-Path $ProjectRoot "kart_racer.csproj")
    if ($projectFile -notmatch [regex]::Escape($ExpectedGodotSdk)) {
        throw "kart_racer.csproj must use $ExpectedGodotSdk."
    }

    $selectedSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $selectedSdk.StartsWith($ExpectedDotnetSdk)) {
        throw "The selected .NET SDK must be $ExpectedDotnetSdk; found '$selectedSdk'."
    }
}

function Invoke-GodotChecked {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [switch]$CheckDiagnostics,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120
    )

    Write-Host "==> $Name"
    $logicRoot = Join-Path $ProjectRoot ("artifacts/harness/logic-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $logicRoot -Force | Out-Null
    $process = Invoke-HarnessProcess -FilePath $script:Godot -ArgumentList $Arguments -WorkingDirectory $ProjectRoot -StdoutPath (Join-Path $logicRoot "stdout.log") -StderrPath (Join-Path $logicRoot "stderr.log") -TimeoutSeconds $TimeoutSeconds
    $outputLines = @((Get-Content -LiteralPath $process.stdout_path) + (Get-Content -LiteralPath $process.stderr_path))
    $outputLines | ForEach-Object { Write-Host $_ }
    if (-not $process.succeeded) {
        throw "$Name failed with exit code $($process.exit_code) (timeout=$($process.timed_out)). Logs: $logicRoot"
    }

    if ($CheckDiagnostics) {
        $diagnostics = @(Get-HarnessDiagnostics -LogPaths @($process.stdout_path, $process.stderr_path))
        if ($diagnostics.Count -gt 0) { throw "$Name emitted diagnostics: $($diagnostics -join '; '). Logs: $logicRoot" }
    }
}

function Invoke-Build {
    Write-Host "==> C# build"
    & dotnet build (Join-Path $ProjectRoot "kart_racer.sln") --nologo --warnaserror
    if ($LASTEXITCODE -ne 0) {
        throw "C# build failed with exit code $LASTEXITCODE."
    }
}

function ConvertTo-ProjectResourcePath {
    param([string]$Path)

    $absolutePath = if ([System.IO.Path]::IsPathRooted($Path)) {
        [System.IO.Path]::GetFullPath($Path)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path))
    }
    $projectRootPath = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd([char[]]@('\', '/'))
    $projectRootPrefix = $projectRootPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $absolutePath.StartsWith($projectRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Capture output must be inside the project directory."
    }
    $relativePath = $absolutePath.Substring($projectRootPrefix.Length)
    return "res://" + $relativePath.Replace("\", "/")
}

$script:Godot = Resolve-HarnessGodotExecutable -RequestedPath $GodotPath

switch ($Command) {
    "verify" {
        Assert-ToolchainConfiguration
        Write-Host "Toolchain verification passed."
    }
    "build" {
        Assert-ToolchainConfiguration
        Invoke-Build
    }
    "test" {
        Assert-ToolchainConfiguration
        # A fresh CI checkout needs enough editor frames to finish every
        # asynchronous texture, model, and audio import before smoke tests
        # resolve resources from .godot/imported.
        Invoke-GodotChecked -Name "Godot import" -Arguments @("--headless", "--path", $ProjectRoot, "--import") -CheckDiagnostics
        foreach ($test in Get-ChildItem (Join-Path $ProjectRoot "tests") -Filter "*smoke_test.gd" | Sort-Object Name) {
            if ($SkipWorldGenerationSmoke -and $test.Name -eq "road_generation_smoke_test.gd") {
                Write-Host "Skipping known world-generation regression until #7 lands."
                continue
            }
            Invoke-GodotChecked -Name $test.Name -Arguments @("--headless", "--path", $ProjectRoot, "--script", "res://tests/$($test.Name)") -CheckDiagnostics
        }
        Invoke-GodotChecked -Name "180-frame runtime boot" -Arguments @("--headless", "--path", $ProjectRoot, "--quit-after", "180") -CheckDiagnostics
    }
    "capture" {
        Assert-ToolchainConfiguration
        if ($Headless) {
            throw "Visual capture requires a rendering display; do not use -Headless."
        }
        $manifest = Invoke-PainTaxiHarness -ProjectRoot $ProjectRoot -GodotPath $Godot -State $State -Suite $Suite -Resolution $Resolution -Seed $Seed -TimeoutSeconds $TimeoutSeconds -Output $Output -SetupMode $SetupMode -SkipBuild:$SkipBuild
        Write-Host "Harness manifest: $($manifest.run_id) ($($manifest.status))"
    }
    "launch" {
        Assert-ToolchainConfiguration
        $launchArguments = @("--path", $ProjectRoot)
        if ($Headless) {
            $launchArguments += "--headless"
        }
        & $script:Godot @launchArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Godot launch failed with exit code $LASTEXITCODE."
        }
    }
}
