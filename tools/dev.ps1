[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("verify", "build", "test", "capture", "launch")]
    [string]$Command = "verify",
    [string]$GodotPath,
    [ValidateSet("menu", "gameplay", "vehicle", "boarding", "dropoff")]
    [string]$State = "menu",
    [string]$Output,
    [switch]$Headless,
    [switch]$SkipWorldGenerationSmoke
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
# Godot writes warnings to stderr even when it exits successfully.  We inspect
# the collected diagnostics ourselves, so stderr must not become a terminating
# PowerShell native-command error before the exit code is checked.
$PSNativeCommandUseErrorActionPreference = $false

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
    if ($projectConfig -notmatch 'config/features=PackedStringArray\("4\.6", "C#", "Forward Plus"\)') {
        throw "project.godot must target Godot 4.6 with C# and Forward Plus."
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
        [switch]$CheckDiagnostics
    )

    Write-Host "==> $Name"
    $diagnosticsPath = Join-Path ([System.IO.Path]::GetTempPath()) ("pain-taxi-godot-" + [guid]::NewGuid().ToString("N") + ".log")
    & $script:Godot @Arguments *> $diagnosticsPath
    $exitCode = $LASTEXITCODE
    $outputLines = @(Get-Content -LiteralPath $diagnosticsPath)
    Remove-Item -LiteralPath $diagnosticsPath -Force
    $outputLines | ForEach-Object { Write-Host $_ }
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }

    if ($CheckDiagnostics) {
        $output = $outputLines -join [Environment]::NewLine
        $leakPatterns = @(
            '(?im)^SCRIPT ERROR:',
            '(?im)ObjectDB instances leaked at exit',
            '(?im)Leaked instance:',
            '(?im)Resources still in use at exit',
            '(?im)RID allocations'
        )
        foreach ($pattern in $leakPatterns) {
            if ($output -match $pattern) {
                throw "$Name emitted a script error or resource-leak diagnostic: $pattern"
            }
        }
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

$script:Godot = Resolve-GodotExecutable -RequestedPath $GodotPath

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
        Invoke-GodotChecked -Name "Godot import" -Arguments @("--headless", "--path", $ProjectRoot, "--editor", "--quit-after", "1800") -CheckDiagnostics
        foreach ($test in Get-ChildItem (Join-Path $ProjectRoot "tests") -Filter "*smoke_test.gd" | Sort-Object Name) {
            if ($SkipWorldGenerationSmoke -and $test.Name -eq "road_generation_smoke_test.gd") {
                Write-Host "Skipping known world-generation regression until #7 lands."
                continue
            }
            Invoke-GodotChecked -Name $test.Name -Arguments @("--headless", "--path", $ProjectRoot, "--script", "res://tests/$($test.Name)")
        }
        Invoke-GodotChecked -Name "180-frame runtime boot" -Arguments @("--headless", "--path", $ProjectRoot, "--quit-after", "180")
    }
    "capture" {
        Assert-ToolchainConfiguration
        if ($Headless) {
            throw "Visual capture requires a rendering display; do not use -Headless."
        }
        if ([string]::IsNullOrWhiteSpace($Output)) {
            $Output = "artifacts/visual/captures/$State.png"
        }
        $resourceOutput = ConvertTo-ProjectResourcePath -Path $Output
        $absoluteOutput = Join-Path $ProjectRoot ($resourceOutput.Substring(6).Replace("/", [System.IO.Path]::DirectorySeparatorChar))
        if (Test-Path -LiteralPath $absoluteOutput) {
            Remove-Item -LiteralPath $absoluteOutput -Force
        }
        Invoke-GodotChecked -Name "visual capture ($State)" -Arguments @("--path", $ProjectRoot, "--script", "res://tests/visual_capture.gd", "--", "--state=$State", "--output=$resourceOutput") -CheckDiagnostics
        if (-not (Test-Path -LiteralPath $absoluteOutput -PathType Leaf) -or (Get-Item -LiteralPath $absoluteOutput).Length -le 0) {
            throw "Visual capture was not written: $absoluteOutput"
        }
        Write-Host "Visual capture verified: $absoluteOutput"
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
