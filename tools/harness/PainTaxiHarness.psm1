Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-HarnessProjectRoot {
    [CmdletBinding()]
    param([string]$Root)

    if ([string]::IsNullOrWhiteSpace($Root)) {
        $Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    }
    return [System.IO.Path]::GetFullPath($Root)
}

function Resolve-HarnessGodotExecutable {
    [CmdletBinding()]
    param([string]$RequestedPath)

    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) { $candidates.Add($RequestedPath) }
    if (-not [string]::IsNullOrWhiteSpace($env:GODOT)) { $candidates.Add($env:GODOT) }
    foreach ($name in @('godot', 'godot4')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($null -ne $command) { $candidates.Add($command.Source) }
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $root = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.6.3-stable_mono_win64'
        $candidates.Add((Join-Path $root 'Godot_v4.6.3-stable_mono_win64_console.exe'))
        $candidates.Add((Join-Path $root 'Godot_v4.6.3-stable_mono_win64.exe'))
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        $resolved = $null
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $resolved = (Resolve-Path -LiteralPath $candidate).Path
        } else {
            $command = Get-Command $candidate -ErrorAction SilentlyContinue
            if ($null -ne $command) { $resolved = $command.Source }
        }
        if ($null -eq $resolved) { continue }
        # ProcessStartInfo.UseShellExecute=false intentionally rejects command
        # shims (.cmd/.bat/.ps1). Keep searching for the pinned native Mono exe.
        if ([System.IO.Path]::GetExtension($resolved).ToLowerInvariant() -in @('.cmd', '.bat', '.ps1', '.sh')) { continue }
        $version = (& $resolved '--version' 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -eq 0 -and $version -match '(?i)4\.6\.3.*mono') {
            return $resolved
        }
    }
    throw 'Godot 4.6.3 Mono was not found. Pass -GodotPath <executable>, set GODOT, or install the pinned Mono build.'
}

function ConvertTo-HarnessAbsolutePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ProjectRoot
    )
    if ($Path.StartsWith('res://', [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path = $Path.Substring(6)
    }
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $Path))
}

function Test-HarnessExcludedPath {
    param([Parameter(Mandatory)][string]$RelativePath)
    $normalized = $RelativePath.Replace('\', '/').TrimStart('/')
    if ($normalized -match '(?i)\.uid$') { return $true }
    $parts = $normalized.Split('/')
    $excludedDirectories = @('.git', '.godot', 'artifacts', 'vendor', 'bin', 'obj', 'node_modules', '__pycache__', '.hermes', 'scratch', 'tmp')
    foreach ($part in $parts) {
        if ($excludedDirectories -contains $part) { return $true }
    }
    if ($normalized -match '(^|/)tools/godot-mcp-runtime/node_modules(/|$)') { return $true }
    if ($normalized -match '(^|/)(generated|build|dist)(/|$)') { return $true }
    return $false
}

function Get-HarnessFileHash {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '<deleted>' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-HarnessSourceIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot)

    $head = (& git -C $ProjectRoot rev-parse HEAD 2>$null).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) { $head = 'NO_GIT_HEAD' }
    $statusArgs = @('-C', $ProjectRoot, 'status', '--porcelain=v1', '-z', '--untracked-files=all', '--', '.', ':(exclude)artifacts/**', ':(exclude).godot/**', ':(exclude).git/**', ':(exclude)vendor/**', ':(exclude)bin/**', ':(exclude)obj/**', ':(exclude)node_modules/**', ':(exclude)tools/godot-mcp-runtime/node_modules/**', ':(exclude).harness/**', ':(exclude)scratch/**', ':(exclude)tmp/**')
    $statusBytes = [System.Text.Encoding]::UTF8.GetBytes((& git @statusArgs 2>$null | Out-String))
    $statusText = [System.Text.Encoding]::UTF8.GetString($statusBytes)
    $records = $statusText -split "`0" | Where-Object { $_ -ne '' }
    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($record in $records) {
        if ($record.Length -lt 4) { continue }
        $status = $record.Substring(0, 2)
        $path = $record.Substring(3)
        if ($status -match '^(R|C)' -and $path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
        if (Test-HarnessExcludedPath -RelativePath $path) { continue }
        if ($status -eq '??' -and $path -match '(?i)\.import$') { continue }
        $absolute = Join-Path $ProjectRoot ($path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        $hash = Get-HarnessFileHash -Path $absolute
        $entries.Add("$status|$($path.Replace('\', '/'))|$hash")
    }
    $canonical = @($head) + @($entries | Sort-Object)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($canonical -join "`n"))
    $fingerprint = ([System.Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
    [pscustomobject]@{
        head = $head
        dirty = ($entries.Count -gt 0)
        dirty_files = @($entries | Sort-Object)
        fingerprint = $fingerprint
    }
}

function Find-HarnessAssembly {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot)

    $candidates = @(@(
        (Join-Path $ProjectRoot '.godot/mono/temp/bin/Debug/kart_racer.dll'),
        (Join-Path $ProjectRoot 'bin/Debug/net8.0/kart_racer.dll'),
        (Join-Path $ProjectRoot '.godot/mono/temp/bin/Release/kart_racer.dll'),
        (Join-Path $ProjectRoot 'bin/Release/net8.0/kart_racer.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($candidates.Count -eq 0) { return $null }
    return $candidates[0]
}

function Get-HarnessAssemblyIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $assembly = Find-HarnessAssembly -ProjectRoot $ProjectRoot
    if ($null -eq $assembly) {
        return [pscustomobject]@{ path = $null; hash = $null; last_write_utc = $null }
    }
    $item = Get-Item -LiteralPath $assembly
    [pscustomobject]@{
        path = $item.FullName
        hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        last_write_utc = $item.LastWriteTimeUtc.ToString('o')
    }
}

function Read-HarnessRegistry {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProjectRoot)
    $path = Join-Path $ProjectRoot 'tests/harness/scenarios.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Scenario registry is missing: $path" }
    try { $registry = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json } catch { throw "Scenario registry is not valid JSON: $($_.Exception.Message)" }
    if ($registry.schema_version -ne 1) { throw "Scenario registry schema_version must be 1." }
    if ($null -eq $registry.states -or @($registry.states.PSObject.Properties).Count -eq 0) { throw 'Scenario registry must declare at least one state.' }
    if ($null -eq $registry.suites) { throw 'Scenario registry must declare suites.' }
    return $registry
}

function ConvertTo-HarnessResolution {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Resolution)
    if ($Resolution -notmatch '^([1-9][0-9]{2,5})x([1-9][0-9]{2,5})$') { throw "Resolution must be WIDTHxHEIGHT, got '$Resolution'." }
    $width = [int]$Matches[1]; $height = [int]$Matches[2]
    if ($width -lt 320 -or $height -lt 200) { throw 'Resolution is below the supported minimum of 320x200.' }
    [pscustomobject]@{ width = $width; height = $height; text = "${width}x${height}" }
}

function Assert-HarnessInputs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [string]$State,
        [string]$Suite,
        [Parameter(Mandatory)][string]$Resolution,
        [int]$Seed = 1337,
        [int]$TimeoutSeconds = 120,
        [string]$Output,
        [ValidateSet('', 'fixture', 'journey')][string]$SetupMode = ''
    )
    $registry = Read-HarnessRegistry -ProjectRoot $ProjectRoot
    $states = @($registry.states.PSObject.Properties | ForEach-Object { [string]$_.Name })
    $suites = @{}
    foreach ($property in $registry.suites.PSObject.Properties) { $suites[$property.Name] = @($property.Value | ForEach-Object { [string]$_ }) }
    if ([string]::IsNullOrWhiteSpace($State) -and [string]::IsNullOrWhiteSpace($Suite)) { throw 'A state or suite is required.' }
    if (-not [string]::IsNullOrWhiteSpace($State) -and $states -notcontains $State) { throw "Invalid state '$State'. Valid states: $($states -join ', ')." }
    if (-not [string]::IsNullOrWhiteSpace($Suite) -and -not $suites.ContainsKey($Suite)) { throw "Invalid suite '$Suite'. Valid suites: $($suites.Keys -join ', ')." }
    if (-not [string]::IsNullOrWhiteSpace($Suite)) {
        foreach ($suiteState in @($suites[$Suite])) {
            if ($states -notcontains $suiteState) { throw "Suite '$Suite' references invalid state '$suiteState'." }
        }
    }
    $parsedResolution = ConvertTo-HarnessResolution -Resolution $Resolution
    if ($Seed -lt 0) { throw 'Seed must be zero or greater.' }
    if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 86400) { throw 'TimeoutSeconds must be between 1 and 86400.' }
    if (-not [string]::IsNullOrWhiteSpace($Output)) {
        if (-not [string]::IsNullOrWhiteSpace($Suite) -and $Output.EndsWith('.png', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'A suite output must be a directory/root, not a single PNG file.' }
        $absoluteOutput = ConvertTo-HarnessAbsolutePath -Path $Output -ProjectRoot $ProjectRoot
        if (-not [System.IO.Path]::IsPathRooted($Output) -and -not $Output.StartsWith('res://', [System.StringComparison]::OrdinalIgnoreCase)) {
            $projectPrefix = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd([char[]]@([char]92, '/')) + [System.IO.Path]::DirectorySeparatorChar
            if (-not $absoluteOutput.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Relative output traversal is outside the project: $Output" }
        }
        $parent = Split-Path -Parent $absoluteOutput
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        if ((Test-Path -LiteralPath $absoluteOutput -PathType Leaf) -and -not $Output.EndsWith('.png', [System.StringComparison]::OrdinalIgnoreCase)) { throw "Output root must be a directory: $absoluteOutput" }
        if ((Test-Path -LiteralPath $absoluteOutput -PathType Container) -and $Output.EndsWith('.png', [System.StringComparison]::OrdinalIgnoreCase)) { throw "Output PNG path points to a directory: $absoluteOutput" }
    } else { $absoluteOutput = $null }
    [pscustomobject]@{ registry = $registry; states = $states; suites = $suites; resolution = $parsedResolution; output = $absoluteOutput }
}

function New-HarnessRunContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$Resolution,
        [int]$Seed = 1337,
        [string]$OutputRoot
    )
    $runId = '{0:yyyyMMdd-HHmmssfff}-{1}' -f (Get-Date), ([guid]::NewGuid().ToString('N').Substring(0, 8))
    $base = if ([string]::IsNullOrWhiteSpace($OutputRoot)) { Join-Path $ProjectRoot 'artifacts/harness' } else { ConvertTo-HarnessAbsolutePath -Path $OutputRoot -ProjectRoot $ProjectRoot }
    $runDirectory = Join-Path $base $runId
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    foreach ($name in @('logs', 'profiles', 'scenarios')) { New-Item -ItemType Directory -Path (Join-Path $runDirectory $name) -Force | Out-Null }
    [pscustomobject]@{
        run_id = $runId
        root = $runDirectory
        logs = Join-Path $runDirectory 'logs'
        profiles = Join-Path $runDirectory 'profiles'
        scenarios = Join-Path $runDirectory 'scenarios'
        started_utc = (Get-Date).ToUniversalTime()
        resolution = $Resolution
        seed = $Seed
    }
}

function Invoke-HarnessProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Alias('Arguments')][string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$StdoutPath,
        [Parameter(Mandatory)][string]$StderrPath,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120
    )
    foreach ($logPath in @($StdoutPath, $StderrPath)) {
        $parent = Split-Path -Parent $logPath
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    }
    $startUtc = (Get-Date).ToUniversalTime()
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) { [void]$psi.ArgumentList.Add([string]$argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $timedOut = $false
    $cleanupComplete = $false
    $launchError = $null
    $stdout = ''
    $stderr = ''
    try {
        try { [void]$process.Start() } catch { $launchError = $_.Exception.Message }
        if ($null -eq $launchError) {
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
                $timedOut = $true
                try { $process.Kill($true) } catch { }
                $cleanupComplete = $process.WaitForExit(5000)
            } else {
                $cleanupComplete = $true
            }
            if ($stdoutTask.Wait(5000)) { $stdout = $stdoutTask.GetAwaiter().GetResult() }
            if ($stderrTask.Wait(5000)) { $stderr = $stderrTask.GetAwaiter().GetResult() }
        }
    } finally {
        [System.IO.File]::WriteAllText($StdoutPath, $stdout)
        [System.IO.File]::WriteAllText($StderrPath, $stderr)
        $exitCode = -1
        if ($null -eq $launchError) { try { $exitCode = $process.ExitCode } catch { $exitCode = -1 } }
        $process.Dispose()
    }
    [pscustomobject]@{
        file = $FilePath
        arguments = @($ArgumentList)
        exit_code = $exitCode
        timed_out = $timedOut
        cleanup_complete = $cleanupComplete
        launch_error = $launchError
        started_utc = $startUtc
        finished_utc = (Get-Date).ToUniversalTime()
        stdout_path = $StdoutPath
        stderr_path = $StderrPath
        succeeded = ($null -eq $launchError -and -not $timedOut -and $exitCode -eq 0)
    }
}

function Get-HarnessPngDimensions {
    param([Parameter(Mandatory)][string]$Path)
    if (-not ('PainTaxiHarness.PngIntegrity' -as [type])) {
        Add-Type -TypeDefinition (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'PngIntegrity.cs.txt'))
    }
    $dimensions = [PainTaxiHarness.PngIntegrity]::Validate($Path)
    return [pscustomobject]@{ width = $dimensions[0]; height = $dimensions[1] }
}
function ConvertTo-HarnessSafeHtml {
    param([AllowNull()][object]$Value)
    return [System.Net.WebUtility]::HtmlEncode([string]$Value)
}

function Test-HarnessJsonBoolean {
    param([AllowNull()][object]$Value)
    return ($Value -is [bool])
}

function Test-HarnessResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ResultPath,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$State,
        [Parameter(Mandatory)][string]$ExpectedOutput,
        [Parameter(Mandatory)][datetime]$StartedUtc,
        [Parameter(Mandatory)][int]$ExpectedWidth,
        [Parameter(Mandatory)][int]$ExpectedHeight,
        [int]$ExpectedArtifactCount = 1,
        [string]$ExpectedResolution,
        [int]$ExpectedSeed = -1,
        [string]$ExpectedSetupMode,
        [string]$ExpectedSourceFingerprint,
        [string]$ExpectedAssemblyHash
    )
    # Result JSON is an untrusted process boundary. Missing properties must be
    # reported as contract errors rather than terminating the supervisor under
    # the module's strict mode.
    Set-StrictMode -Off
    $errors = [System.Collections.Generic.List[string]]::new()
    $result = $null
    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) { $errors.Add("Result record was not written: $ResultPath") }
    else {
        if ((Get-Item -LiteralPath $ResultPath).LastWriteTimeUtc -lt $StartedUtc) { $errors.Add('Result record is stale.') }
        try { $result = Get-Content -Raw -LiteralPath $ResultPath | ConvertFrom-Json } catch { $errors.Add("Result record is invalid JSON: $($_.Exception.Message)") }
    }
    if ($null -ne $result) {
        if ($result.schema_version -ne 1) { $errors.Add('Result schema_version must be 1.') }
        if ([string]$result.run_id -ne $RunId) { $errors.Add("Result run_id does not match '$RunId'.") }
        if ([string]$result.state -ne $State) { $errors.Add("Result state does not match '$State'.") }
        if ([string]$result.status -ne 'passed') { $errors.Add("Result status is '$($result.status)'.") }
        if ($null -eq $result.observations -or @($result.observations.PSObject.Properties).Count -eq 0) { $errors.Add('Result observations{} is missing or empty.') }
        if ($ExpectedSeed -ge 0) {
            if ($null -eq $result.seed) { $errors.Add('Result seed is missing.') }
            else { try { if ([int]$result.seed -ne $ExpectedSeed) { $errors.Add("Result seed does not match '$ExpectedSeed'.") } } catch { $errors.Add('Result seed is malformed.') } }
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedResolution)) {
            if ($null -eq $result.resolution) { $errors.Add('Result resolution is missing.') }
            else {
                try {
                    $actualResolution = "{0}x{1}" -f [int]$result.resolution.width, [int]$result.resolution.height
                    if ($actualResolution -ne $ExpectedResolution) { $errors.Add("Result resolution does not match '$ExpectedResolution'.") }
                } catch { $errors.Add('Result resolution is malformed.') }
            }
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedSetupMode)) {
            if ($null -eq $result.setup_mode) { $errors.Add('Result setup_mode is missing.') }
            elseif ([string]$result.setup_mode -ne $ExpectedSetupMode) { $errors.Add("Result setup_mode does not match '$ExpectedSetupMode'.") }
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceFingerprint) -and $null -ne $result.source_fingerprint -and [string]$result.source_fingerprint -ne $ExpectedSourceFingerprint) { $errors.Add('Result source fingerprint does not match the supervisor identity.') }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedAssemblyHash) -and $null -ne $result.assembly_hash -and [string]$result.assembly_hash -ne $ExpectedAssemblyHash) { $errors.Add('Result assembly hash does not match the built assembly.') }
        if ($null -eq $result.errors) { $errors.Add('Result errors[] is missing.') } elseif (@($result.errors).Count -gt 0) { $errors.Add("Result reported errors: $(@($result.errors) -join '; ')") }
        if (-not (Test-HarnessJsonBoolean $result.cleanup_complete) -or $result.cleanup_complete -ne $true) { $errors.Add('Result cleanup_complete must be the JSON boolean true.') }
        if ($null -eq $result.assertions -or @($result.assertions).Count -eq 0) { $errors.Add('Result assertions[] is missing or empty.') } else {
            foreach ($assertion in @($result.assertions)) { if (-not (Test-HarnessJsonBoolean $assertion.passed) -or $assertion.passed -ne $true) { $errors.Add("Assertion failed: $($assertion.name)") } }
        }
        if ($null -eq $result.artifacts -or @($result.artifacts).Count -eq 0) { $errors.Add('Result artifacts[] is missing or empty.') }
        else {
            $artifactPaths = @($result.artifacts | ForEach-Object { [string]$_.path })
            if (-not ($artifactPaths | Where-Object { $_.Equals($ExpectedOutput, [System.StringComparison]::OrdinalIgnoreCase) })) { $errors.Add('Result artifacts[] does not identify the expected output path.') }
            $pngArtifacts = @($result.artifacts | Where-Object { [string]$_.kind -match '(?i)png|capture|contact_sheet' })
            $distinctPngPaths = @($pngArtifacts | ForEach-Object { [string]$_.path } | Sort-Object -Unique)
            if ($distinctPngPaths.Count -lt $ExpectedArtifactCount) { $errors.Add("Result contains $($distinctPngPaths.Count) distinct PNG artifacts; expected at least $ExpectedArtifactCount.") }
            foreach ($artifact in @($result.artifacts)) {
                $artifactPath = [string]$artifact.path
                if ([string]::IsNullOrWhiteSpace($artifactPath) -or -not [System.IO.Path]::IsPathRooted($artifactPath)) { $errors.Add('Result artifact paths must be absolute.'); continue }
                if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) { $errors.Add("Result artifact was not written: $artifactPath"); continue }
                $artifactItem = Get-Item -LiteralPath $artifactPath
                if ($artifactItem.Length -le 0 -or $artifactItem.LastWriteTimeUtc -lt $StartedUtc) { $errors.Add("Result artifact is empty or stale: $artifactPath"); continue }
                if ([System.IO.Path]::GetExtension($artifactPath).Equals('.png', [System.StringComparison]::OrdinalIgnoreCase)) {
                    try { $artifactDimensions = Get-HarnessPngDimensions -Path $artifactPath; if ($artifactDimensions.width -ne $ExpectedWidth -or $artifactDimensions.height -ne $ExpectedHeight) { $errors.Add("PNG artifact dimensions were $($artifactDimensions.width)x$($artifactDimensions.height), expected ${ExpectedWidth}x${ExpectedHeight}.") } } catch { $errors.Add("PNG artifact is unreadable: $artifactPath") }
                }
            }
        }
    }
    if (-not (Test-Path -LiteralPath $ExpectedOutput -PathType Leaf)) { $errors.Add("Expected PNG was not written: $ExpectedOutput") }
    else {
        $item = Get-Item -LiteralPath $ExpectedOutput
        if ($item.Length -le 0) { $errors.Add('Expected PNG is empty.') }
        if ($item.LastWriteTimeUtc -lt $StartedUtc) { $errors.Add('Expected PNG is stale.') }
        try {
            $dimensions = Get-HarnessPngDimensions -Path $ExpectedOutput
            if ($dimensions.width -ne $ExpectedWidth -or $dimensions.height -ne $ExpectedHeight) { $errors.Add("PNG dimensions were $($dimensions.width)x$($dimensions.height), expected ${ExpectedWidth}x${ExpectedHeight}.") }
        } catch { $errors.Add("PNG is unreadable: $($_.Exception.Message)") }
    }
    [pscustomobject]@{ passed = ($errors.Count -eq 0); errors = @($errors); result = $result }
}

function Get-HarnessDiagnostics {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$LogPaths)
    $patterns = @(
        '(?im)^SCRIPT ERROR:',
        '(?im)^\s*ERROR:',
        '(?im)ObjectDB instances leaked at exit',
        '(?im)Leaked instance:',
        '(?im)Resources still in use at exit',
        '(?im)RID allocations'
    )
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $LogPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $content = Get-Content -Raw -LiteralPath $path
        foreach ($pattern in $patterns) {
            if ($content -match $pattern) { $found.Add("$([System.IO.Path]::GetFileName($path)): $pattern") }
        }
    }
    return @($found)
}

function Invoke-HarnessBuild {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$SourceIdentity,
        [Parameter(Mandatory)][string]$DotnetPath,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 600,
        [switch]$SkipBuild
    )
    $receiptPath = Join-Path $ProjectRoot '.harness/build-receipt.json'
    if ($SkipBuild) {
        if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw "-SkipBuild requires a cached build receipt at $receiptPath." }
        try { $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json } catch { throw "Cached build receipt is invalid: $($_.Exception.Message)" }
        if ([string]$receipt.source_fingerprint -ne [string]$SourceIdentity.fingerprint) { throw 'Cached build receipt does not match the current source identity.' }
        $assembly = Get-HarnessAssemblyIdentity -ProjectRoot $ProjectRoot
        if ([string]::IsNullOrWhiteSpace($assembly.hash) -or [string]$receipt.assembly_hash -ne [string]$assembly.hash) { throw 'Cached build receipt does not match the current assembly.' }
        return $assembly
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $receiptPath) -Force | Out-Null
    $build = Invoke-HarnessProcess -FilePath $DotnetPath -ArgumentList @('build', (Join-Path $ProjectRoot 'kart_racer.sln'), '--nologo', '--warnaserror') -WorkingDirectory $ProjectRoot -StdoutPath (Join-Path $Context.logs 'build.stdout.log') -StderrPath (Join-Path $Context.logs 'build.stderr.log') -TimeoutSeconds $TimeoutSeconds
    if (-not $build.succeeded) { throw "C# build failed (exit=$($build.exit_code), timeout=$($build.timed_out)). See $($Context.logs)." }
    $assembly = Get-HarnessAssemblyIdentity -ProjectRoot $ProjectRoot
    if ([string]::IsNullOrWhiteSpace($assembly.hash)) { throw 'C# build succeeded but kart_racer.dll was not found.' }
    $receipt = [ordered]@{ schema_version = 1; source_fingerprint = $SourceIdentity.fingerprint; assembly_hash = $assembly.hash; assembly_path = $assembly.path; built_utc = (Get-Date).ToUniversalTime().ToString('o') }
    $receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    return $assembly
}

function New-HarnessHtmlReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$Path)
    $cards = [System.Text.StringBuilder]::new()
    foreach ($scenario in @($Manifest.scenarios)) {
        $status = ConvertTo-HarnessSafeHtml $scenario.status
        $class = if ($scenario.status -eq 'passed') { 'pass' } else { 'fail' }
        $assertions = (($scenario.assertions | ForEach-Object { "<li class='$(if ($_.passed) { 'ok' } else { 'bad' })'>$(ConvertTo-HarnessSafeHtml $_.name): $(ConvertTo-HarnessSafeHtml $_.passed)</li>" }) -join '')
        $relativeImage = if (-not [string]::IsNullOrWhiteSpace([string]$scenario.output)) { ([System.IO.Path]::GetRelativePath((Split-Path -Parent $Path), $scenario.output)).Replace('\', '/') } else { '' }
        $image = if ($relativeImage) { "<img src='$(ConvertTo-HarnessSafeHtml $relativeImage)' alt='$(ConvertTo-HarnessSafeHtml $scenario.name)' />" } else { '' }
        [void]$cards.Append("<article class='card $class'><h2>$(ConvertTo-HarnessSafeHtml $scenario.name) <span>$status</span></h2>$image<h3>Assertions</h3><ul>$assertions</ul><h3>Observations</h3><pre>$(ConvertTo-HarnessSafeHtml (($scenario.observations | ConvertTo-Json -Depth 12)))</pre><p>$(ConvertTo-HarnessSafeHtml (($scenario.errors -join '; ')))</p></article>")
    }
    $html = @"
<!doctype html><html><head><meta charset="utf-8"><title>PAIN TAXI harness $(ConvertTo-HarnessSafeHtml $Manifest.run_id)</title><style>body{font:14px system-ui;background:#10131b;color:#eef1f8;margin:2rem}.summary{padding:1rem;border:1px solid #394258;border-radius:10px}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(360px,1fr));gap:1rem;margin-top:1rem}.card{border:1px solid #394258;border-radius:10px;padding:1rem;background:#181d29}.card h2{display:flex;justify-content:space-between}.pass{border-color:#36c98f}.fail{border-color:#f06b6b}.ok{color:#62e6ae}.bad{color:#ff8989}img{display:block;width:100%;height:auto;border-radius:6px;background:#080a0f}pre{white-space:pre-wrap;overflow:auto;max-height:18rem}</style></head><body><div class="summary"><h1>PAIN TAXI supervised harness</h1><p>Run $(ConvertTo-HarnessSafeHtml $Manifest.run_id) · status $(ConvertTo-HarnessSafeHtml $Manifest.status) · source $(ConvertTo-HarnessSafeHtml $Manifest.source.fingerprint) · assembly $(ConvertTo-HarnessSafeHtml $Manifest.assembly.hash)</p></div><main class="grid">$cards</main></body></html>
"@
    Set-Content -LiteralPath $Path -Value $html -Encoding UTF8
    return $Path
}

function Invoke-PainTaxiHarness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$GodotPath,
        [string]$State,
        [string]$Suite,
        [string]$Resolution = '1920x1080',
        [int]$Seed = 1337,
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120,
        [string]$Output,
        [ValidateSet('', 'fixture', 'journey')][string]$SetupMode = '',
        [switch]$SkipBuild,
        [string]$DotnetPath,
        [string]$Scene,
        [string]$CameraPreset,
        [string]$CameraPos,
        [string]$CameraTarget,
        [double]$CameraFov = 0,
        [int]$VehicleIndex = -1,
        [int]$PixelSize = -1,
        [Nullable[bool]]$Crt,
        [Nullable[bool]]$Scanlines,
        [int]$BurstCount = 1,
        [double]$BurstInterval = 0.2,
        [double]$WaitSeconds = -1,
        [switch]$Visible,
        [switch]$NoContactSheet,
        [switch]$NoMetadata
    )
    $ProjectRoot = Get-HarnessProjectRoot -Root $ProjectRoot
    $inputs = Assert-HarnessInputs -ProjectRoot $ProjectRoot -State $State -Suite $Suite -Resolution $Resolution -Seed $Seed -TimeoutSeconds $TimeoutSeconds -Output $Output -SetupMode $SetupMode
    if ($BurstCount -lt 1) { throw 'BurstCount must be at least 1.' }
    if ($BurstInterval -lt 0) { throw 'BurstInterval must be zero or greater.' }
    if ($WaitSeconds -lt -1) { throw 'WaitSeconds must be -1 or greater.' }
    if ($CameraFov -lt 0 -or $CameraFov -gt 180) { throw 'CameraFov must be between 0 and 180 degrees.' }
    if ($CameraPreset -notin @('', 'default', 'chase', 'hood', 'cockpit', 'birds_eye', 'orbit', 'front', 'side', 'city_high')) { throw "Invalid camera preset '$CameraPreset'." }
    if ($VehicleIndex -lt -1) { throw 'VehicleIndex must be -1 or greater.' }
    if ($PixelSize -eq 0 -or $PixelSize -lt -1) { throw 'PixelSize must be -1 or greater and cannot be zero.' }
    if (-not [string]::IsNullOrWhiteSpace($CameraPos) -and $CameraPos -notmatch '^-?[0-9]+(\.[0-9]+)?,-?[0-9]+(\.[0-9]+)?,-?[0-9]+(\.[0-9]+)?$') { throw 'CameraPos must be x,y,z.' }
    if (-not [string]::IsNullOrWhiteSpace($CameraTarget) -and $CameraTarget -notmatch '^-?[0-9]+(\.[0-9]+)?,-?[0-9]+(\.[0-9]+)?,-?[0-9]+(\.[0-9]+)?$') { throw 'CameraTarget must be x,y,z.' }
    if (-not [string]::IsNullOrWhiteSpace($Scene)) {
        $scenePath = if ($Scene.StartsWith('res://', [System.StringComparison]::OrdinalIgnoreCase)) { ConvertTo-HarnessAbsolutePath -Path $Scene -ProjectRoot $ProjectRoot } else { ConvertTo-HarnessAbsolutePath -Path $Scene -ProjectRoot $ProjectRoot }
        if (-not (Test-Path -LiteralPath $scenePath -PathType Leaf)) { throw "Scene was not found: $Scene" }
    }
    $source = Get-HarnessSourceIdentity -ProjectRoot $ProjectRoot
    if ([string]::IsNullOrWhiteSpace($DotnetPath)) { $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue; if ($null -eq $dotnet) { throw 'dotnet was not found.' }; $DotnetPath = $dotnet.Source }
    $godot = Resolve-HarnessGodotExecutable -RequestedPath $GodotPath
    $contextOutputRoot = if ($Output -and -not $Output.EndsWith('.png', [System.StringComparison]::OrdinalIgnoreCase)) { $Output } else { $null }
    $context = New-HarnessRunContext -ProjectRoot $ProjectRoot -Resolution $inputs.resolution.text -Seed $Seed -OutputRoot $contextOutputRoot
    try {
        $assembly = Invoke-HarnessBuild -ProjectRoot $ProjectRoot -Context $context -SourceIdentity $source -DotnetPath $DotnetPath -TimeoutSeconds ([Math]::Max(600, $TimeoutSeconds)) -SkipBuild:$SkipBuild
        $import = Invoke-HarnessProcess -FilePath $godot -ArgumentList @('--headless', '--path', $ProjectRoot, '--import') -WorkingDirectory $ProjectRoot -StdoutPath (Join-Path $context.logs 'import.stdout.log') -StderrPath (Join-Path $context.logs 'import.stderr.log') -TimeoutSeconds ([Math]::Max(600, $TimeoutSeconds))
        if (-not $import.succeeded) { throw "Godot import failed (exit=$($import.exit_code), timeout=$($import.timed_out)). See $($context.logs)." }
        $importDiagnostics = @(Get-HarnessDiagnostics -LogPaths @($import.stdout_path, $import.stderr_path))
        if ($importDiagnostics.Count -gt 0) { throw "Godot import emitted diagnostics: $($importDiagnostics -join '; ')" }
        $preparedSource = Get-HarnessSourceIdentity -ProjectRoot $ProjectRoot
        if ($preparedSource.fingerprint -ne $source.fingerprint) {
            if ($SkipBuild) { throw 'Source identity changed during build/import; -SkipBuild cannot claim a fresh assembly. Retry after generated files settle.' }
            $source = $preparedSource
            $assembly = Invoke-HarnessBuild -ProjectRoot $ProjectRoot -Context $context -SourceIdentity $source -DotnetPath $DotnetPath -TimeoutSeconds ([Math]::Max(600, $TimeoutSeconds))
            $importRetry = Invoke-HarnessProcess -FilePath $godot -ArgumentList @('--headless', '--path', $ProjectRoot, '--import') -WorkingDirectory $ProjectRoot -StdoutPath (Join-Path $context.logs 'import-retry.stdout.log') -StderrPath (Join-Path $context.logs 'import-retry.stderr.log') -TimeoutSeconds ([Math]::Max(600, $TimeoutSeconds))
            if (-not $importRetry.succeeded) { throw "Godot import retry failed (exit=$($importRetry.exit_code), timeout=$($importRetry.timed_out)). See $($context.logs)." }
            $retryDiagnostics = @(Get-HarnessDiagnostics -LogPaths @($importRetry.stdout_path, $importRetry.stderr_path))
            if ($retryDiagnostics.Count -gt 0) { throw "Godot import retry emitted diagnostics: $($retryDiagnostics -join '; ')" }
            $afterRetry = Get-HarnessSourceIdentity -ProjectRoot $ProjectRoot
            if ($afterRetry.fingerprint -ne $source.fingerprint) { throw 'Source identity changed during repeated preparation; retry after generated files settle.' }
        } else {
            $source = $preparedSource
        }
    } catch {
        $failure = [ordered]@{ schema_version = 1; run_id = $context.run_id; root = $context.root; status = 'failed'; started_utc = $context.started_utc.ToString('o'); finished_utc = (Get-Date).ToUniversalTime().ToString('o'); source = $source; assembly = Get-HarnessAssemblyIdentity -ProjectRoot $ProjectRoot; resolution = $inputs.resolution.text; seed = $Seed; suite = $Suite; state = $State; scenarios = @(); logs = $context.logs; errors = @($_.Exception.Message); evidence_kind = 'visual' }
        $failurePath = Join-Path $context.root 'manifest.json'; $failure | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $failurePath -Encoding UTF8
        New-HarnessHtmlReport -Manifest ([pscustomobject]$failure) -Path (Join-Path $context.root 'report.html') | Out-Null
        throw
    }
    $states = if (-not [string]::IsNullOrWhiteSpace($Suite)) { @($inputs.suites[$Suite]) } else { @($State) }
    $scenarioResults = [System.Collections.Generic.List[object]]::new()
    foreach ($scenarioState in $states) {
        $scenarioName = [string]$scenarioState
        $scenarioDir = Join-Path $context.scenarios ($scenarioName -replace '[^A-Za-z0-9_.-]', '_')
        New-Item -ItemType Directory -Path $scenarioDir -Force | Out-Null
        $requestedOutputPath = if (-not [string]::IsNullOrWhiteSpace($Output) -and $states.Count -eq 1 -and $Output.EndsWith('.png', [System.StringComparison]::OrdinalIgnoreCase)) { ConvertTo-HarnessAbsolutePath -Path $Output -ProjectRoot $ProjectRoot } else { $null }
        $outputPath = Join-Path $scenarioDir "$scenarioName.png"
        $resultPath = Join-Path $scenarioDir 'result.json'
        $profileDir = Join-Path $context.profiles $scenarioName
        New-Item -ItemType Directory -Path $profileDir -Force | Out-Null
        $scenarioSetupMode = if (-not [string]::IsNullOrWhiteSpace($SetupMode)) { $SetupMode } else { [string]$inputs.registry.states.PSObject.Properties[$scenarioName].Value.setup }
        if ($scenarioSetupMode -notin @('fixture', 'journey')) { throw "Scenario '$scenarioName' has no valid setup mode." }
        $args = [System.Collections.Generic.List[string]]::new()
        foreach ($item in @('--path', $ProjectRoot, '--audio-driver', 'Dummy', '--fixed-fps', '60', '--rendering-method', 'gl_compatibility')) { $args.Add($item) }
        if (-not $Visible) { [void]$args.Add('--position'); [void]$args.Add('-9999,-9999') }
        foreach ($item in @('--script', 'res://tests/visual_capture_harness.gd', '--', "--state=$scenarioName", "--output=$outputPath", "--seed=$Seed", "--resolution=$Resolution", "--run-id=$($context.run_id)", "--result=$resultPath", "--profile-dir=$profileDir", "--setup-mode=$scenarioSetupMode", "--timeout-seconds=$TimeoutSeconds")) { $args.Add($item) }
        if ($Visible) { [void]$args.Add('--visible') }
        if ($Scene) { [void]$args.Add("--scene=$Scene") }; if ($CameraPreset) { [void]$args.Add("--camera-preset=$CameraPreset") }; if ($CameraPos) { [void]$args.Add("--camera-pos=$CameraPos") }; if ($CameraTarget) { [void]$args.Add("--camera-target=$CameraTarget") }; if ($CameraFov -gt 0) { [void]$args.Add("--camera-fov=$CameraFov") }; if ($VehicleIndex -ge 0) { [void]$args.Add("--vehicle-index=$VehicleIndex") }; if ($PixelSize -gt 0) { [void]$args.Add("--pixel-size=$PixelSize") }; if ($null -ne $Crt) { [void]$args.Add("--crt=$([bool]$Crt)") }; if ($null -ne $Scanlines) { [void]$args.Add("--scanlines=$([bool]$Scanlines)") }; if ($BurstCount -gt 1) { [void]$args.Add("--burst-count=$BurstCount"); [void]$args.Add("--burst-interval=$BurstInterval") }; if ($WaitSeconds -ge 0) { [void]$args.Add("--wait-seconds=$WaitSeconds") }
        if ($NoMetadata) { Write-Warning '-NoMetadata is retained for compatibility but cannot disable authoritative result validation.' }
        $process = Invoke-HarnessProcess -FilePath $godot -ArgumentList @($args) -WorkingDirectory $ProjectRoot -StdoutPath (Join-Path $scenarioDir 'stdout.log') -StderrPath (Join-Path $scenarioDir 'stderr.log') -TimeoutSeconds $TimeoutSeconds
        try {
            $validation = Test-HarnessResult -ResultPath $resultPath -RunId $context.run_id -State $scenarioName -ExpectedOutput $outputPath -StartedUtc $process.started_utc -ExpectedWidth $inputs.resolution.width -ExpectedHeight $inputs.resolution.height -ExpectedArtifactCount $BurstCount -ExpectedResolution $inputs.resolution.text -ExpectedSeed $Seed -ExpectedSetupMode $scenarioSetupMode -ExpectedSourceFingerprint $source.fingerprint -ExpectedAssemblyHash $assembly.hash
        } catch {
            $validation = [pscustomobject]@{ passed = $false; errors = @("Result validation raised an exception: $($_.Exception.Message)"); result = $null }
        }
        $errors = [System.Collections.Generic.List[string]]::new(); foreach ($errorText in @($validation.errors)) { $errors.Add([string]$errorText) }; if (-not $process.succeeded) { $errors.Add("Engine process failed with exit=$($process.exit_code), timeout=$($process.timed_out).") }
        foreach ($diagnostic in @(Get-HarnessDiagnostics -LogPaths @($process.stdout_path, $process.stderr_path))) { $errors.Add("Diagnostic: $diagnostic") }
        if ($errors.Count -eq 0 -and $null -ne $requestedOutputPath) {
            try {
                $requestedParent = Split-Path -Parent $requestedOutputPath; if (-not (Test-Path -LiteralPath $requestedParent -PathType Container)) { New-Item -ItemType Directory -Path $requestedParent -Force | Out-Null }
                Copy-Item -LiteralPath $outputPath -Destination $requestedOutputPath -Force
            } catch { $errors.Add("Legacy output copy failed: $($_.Exception.Message)") }
        }
        $reportedAssertions = @(); $reportedObservations = @{}; $reportedCleanup = $false
        if ($null -ne $validation.result) {
            $property = $validation.result.PSObject.Properties['assertions']
            if ($null -ne $property) { $reportedAssertions = @($property.Value) }
            $property = $validation.result.PSObject.Properties['observations']
            if ($null -ne $property) { $reportedObservations = $property.Value }
            $property = $validation.result.PSObject.Properties['cleanup_complete']
            if ($null -ne $property) { $reportedCleanup = ($property.Value -is [bool] -and $property.Value -eq $true) }
        }
        $scenarioResults.Add([pscustomobject]@{ name = $scenarioName; state = $scenarioName; status = if ($errors.Count -eq 0) { 'passed' } else { 'failed' }; output = $outputPath; requested_output = $requestedOutputPath; result_path = $resultPath; assertions = $reportedAssertions; observations = $reportedObservations; errors = @($errors); stdout_path = $process.stdout_path; stderr_path = $process.stderr_path; cleanup_complete = $reportedCleanup })
    }
    $endSource = Get-HarnessSourceIdentity -ProjectRoot $ProjectRoot
    $sourceDrift = $endSource.fingerprint -ne $source.fingerprint
    if ($sourceDrift) { $scenarioResults.Add([pscustomobject]@{ name = '__source_identity__'; state = ''; status = 'failed'; output = ''; requested_output = $null; result_path = ''; assertions = @(); observations = @{}; errors = @('Source identity changed while the harness was running.'); stdout_path = ''; stderr_path = ''; cleanup_complete = $false }) }
    $manifest = [ordered]@{ schema_version = 1; run_id = $context.run_id; root = $context.root; status = if (@($scenarioResults | Where-Object status -eq 'failed').Count -eq 0) { 'passed' } else { 'failed' }; started_utc = $context.started_utc.ToString('o'); finished_utc = (Get-Date).ToUniversalTime().ToString('o'); source = $source; source_end = $endSource; assembly = $assembly; resolution = $inputs.resolution.text; seed = $Seed; suite = $Suite; state = $State; scenarios = @($scenarioResults); logs = $context.logs; evidence_kind = 'visual' }
    $manifestPath = Join-Path $context.root 'manifest.json'; $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $reportPath = New-HarnessHtmlReport -Manifest ([pscustomobject]$manifest) -Path (Join-Path $context.root 'report.html')
    Write-Host "Harness run $($manifest.status): $($context.root)"
    if ($manifest.status -ne 'passed') { throw "Harness run failed. See $manifestPath and $reportPath." }
    return [pscustomobject]$manifest
}

Export-ModuleMember -Function Get-HarnessProjectRoot, Resolve-HarnessGodotExecutable, ConvertTo-HarnessAbsolutePath, Get-HarnessSourceIdentity, Get-HarnessAssemblyIdentity, Read-HarnessRegistry, ConvertTo-HarnessResolution, Assert-HarnessInputs, New-HarnessRunContext, Invoke-HarnessProcess, Get-HarnessDiagnostics, Get-HarnessPngDimensions, Test-HarnessResult, Invoke-HarnessBuild, New-HarnessHtmlReport, Invoke-PainTaxiHarness
