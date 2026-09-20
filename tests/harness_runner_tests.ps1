[CmdletBinding()]
param([switch]$KeepArtifacts)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modulePath = Join-Path $projectRoot 'tools/harness/PainTaxiHarness.psm1'
$fixturePath = Join-Path $projectRoot 'tests/fixtures/harness/fake_process.ps1'
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
Import-Module -Name $modulePath -Force

$failures = [System.Collections.Generic.List[string]]::new()
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { $failures.Add($Message) }
}
function Assert-Equal {
    param([object]$Actual, [object]$Expected, [string]$Message)
    if ($Actual -ne $Expected) { $failures.Add("$Message (actual='$Actual', expected='$Expected')") }
}
function Assert-Throws {
    param([scriptblock]$Action, [string]$Message)
    $threw = $false
    try { & $Action } catch { $threw = $true }
    if (-not $threw) { $failures.Add($Message) }
}
function New-TestDirectory {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("pain-taxi-harness-{0}" -f [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($path) | Out-Null
    return $path
}
function Invoke-FakeProcess {
    param(
        [Parameter(Mandatory)][string]$OutputDirectory,
        [string]$Mode = 'pass',
        [string]$State = 'gameplay',
        [string]$RunId = 'fixture-run',
        [int]$TimeoutSeconds = 5,
        [string[]]$ExtraArguments = @()
    )
    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    $stdout = Join-Path $OutputDirectory 'stdout.log'
    $stderr = Join-Path $OutputDirectory 'stderr.log'
    $pwshCommand = Get-Command pwsh -ErrorAction Stop
    $pwsh = $pwshCommand.Source
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $fixturePath, '-Mode', $Mode, '-State', $State, '-RunId', $RunId, '-OutputDir', $OutputDirectory) + $ExtraArguments
    return Invoke-HarnessProcess -FilePath $pwsh -ArgumentList $arguments -WorkingDirectory $projectRoot -StdoutPath $stdout -StderrPath $stderr -TimeoutSeconds $TimeoutSeconds
}
function Write-ValidPng {
    param([Parameter(Mandatory)][string]$Path)
    $bytes = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEUlEQVR4nGP4z/D/PwgzwBgAaagL9TZTdecAAAAASUVORK5CYII=')
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

$root = New-TestDirectory
try {
    # A normal child process produces a fresh result, a readable artifact, and
    # preserves stdout/stderr in the run directory.
    $passDir = Join-Path $root 'pass'
    $pass = Invoke-FakeProcess -OutputDirectory $passDir -RunId 'pass-run' -ExtraArguments @('-Crt', 'true', '-Scanlines', 'false')
    Assert-True $pass.succeeded 'A zero-exit fake process should be successful.'
    Assert-True (Test-Path -LiteralPath $pass.stdout_path -PathType Leaf) 'stdout log should be retained.'
    Assert-True (Test-Path -LiteralPath $pass.stderr_path -PathType Leaf) 'stderr log should be retained.'
    $record = Test-HarnessResult -ResultPath (Join-Path $passDir 'result.json') -RunId 'pass-run' -State 'gameplay' -ExpectedOutput (Join-Path $passDir 'capture.png') -StartedUtc $pass.started_utc -ExpectedWidth 2 -ExpectedHeight 2
    Assert-True $record.passed "A complete result contract should pass: $($record.errors -join '; ')"
    $observed = Get-Content -Raw -LiteralPath (Join-Path $passDir 'result.json') | ConvertFrom-Json
    Assert-Equal $observed.observations.crt $true 'Boolean CRT option should reach the child process.'
    Assert-Equal $observed.observations.scanlines $false 'Boolean scanline option should reach the child process.'

    # A diagnostic error is rejected by the shared diagnostic detector even
    # when the child exits zero.
    $diagnostic = Invoke-FakeProcess -OutputDirectory (Join-Path $root 'diagnostic') -Mode diagnostic-error
    $diagnosticFindings = @(Get-HarnessDiagnostics -LogPaths @($diagnostic.stdout_path, $diagnostic.stderr_path))
    Assert-True ($diagnosticFindings.Count -gt 0) 'A diagnostic error must reject an otherwise zero-exit process.'
    Assert-True ((Get-Content -Raw -LiteralPath $diagnostic.stderr_path) -match 'SCRIPT ERROR:') 'Diagnostic stderr must be preserved for review.'

    # Timeout must terminate the child and preserve both log paths.
    $timeout = Invoke-FakeProcess -OutputDirectory (Join-Path $root 'timeout') -Mode timeout -TimeoutSeconds 1 -ExtraArguments @('-SleepSeconds', '10')
    Assert-True $timeout.timed_out 'A child exceeding the timeout must be marked timed_out.'
    Assert-True (-not $timeout.succeeded) 'A timed-out child must be unsuccessful.'
    Assert-True (Test-Path -LiteralPath $timeout.stdout_path -PathType Leaf) 'Timeout stdout log must be retained.'
    Assert-True (Test-Path -LiteralPath $timeout.stderr_path -PathType Leaf) 'Timeout stderr log must be retained.'

    # Result validation rejects missing records, stale captures, invalid state,
    # and artifact paths that were claimed but never written.
    $missingDir = Join-Path $root 'missing-result'
    $missing = Invoke-FakeProcess -OutputDirectory $missingDir -Mode missing-result
    $missingCheck = Test-HarnessResult -ResultPath (Join-Path $missingDir 'result.json') -RunId 'fixture-run' -State 'gameplay' -ExpectedOutput (Join-Path $missingDir 'capture.png') -StartedUtc $missing.started_utc -ExpectedWidth 2 -ExpectedHeight 2
    Assert-True (-not $missingCheck.passed) 'A missing result record must fail validation.'

    $staleDir = Join-Path $root 'stale-result'
    [System.IO.Directory]::CreateDirectory($staleDir) | Out-Null
    $stalePng = Join-Path $staleDir 'capture.png'
    Write-ValidPng -Path $stalePng
    [System.IO.File]::SetLastWriteTimeUtc($stalePng, (Get-Date).ToUniversalTime().AddMinutes(-5))
    $staleJson = [ordered]@{ schema_version = 1; run_id = 'fixture-run'; state = 'gameplay'; status = 'passed'; errors = @(); assertions = @([ordered]@{ name = 'old'; passed = $true; observed = 'old' }); artifacts = @([ordered]@{ kind = 'capture'; path = $stalePng }); observations = @{}; cleanup_complete = $true }
    $staleJson | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $staleDir 'result.json') -Encoding UTF8
    $staleCheck = Test-HarnessResult -ResultPath (Join-Path $staleDir 'result.json') -RunId 'fixture-run' -State 'gameplay' -ExpectedOutput $stalePng -StartedUtc (Get-Date).ToUniversalTime() -ExpectedWidth 2 -ExpectedHeight 2
    Assert-True (-not $staleCheck.passed) 'A stale capture must fail validation.'

    $invalidDir = Join-Path $root 'invalid-state'
    $invalidProcess = Invoke-FakeProcess -OutputDirectory $invalidDir -Mode invalid-state
    $invalidCheck = Test-HarnessResult -ResultPath (Join-Path $invalidDir 'result.json') -RunId 'fixture-run' -State 'gameplay' -ExpectedOutput (Join-Path $invalidDir 'capture.png') -StartedUtc $invalidProcess.started_utc -ExpectedWidth 2 -ExpectedHeight 2
    Assert-True (-not $invalidCheck.passed) 'An observed state mismatch must fail validation.'

    $artifactDir = Join-Path $root 'missing-artifact'
    $artifactProcess = Invoke-FakeProcess -OutputDirectory $artifactDir -Mode missing-artifact
    $artifactCheck = Test-HarnessResult -ResultPath (Join-Path $artifactDir 'result.json') -RunId 'fixture-run' -State 'gameplay' -ExpectedOutput (Join-Path $artifactDir 'capture.png') -StartedUtc $artifactProcess.started_utc -ExpectedWidth 2 -ExpectedHeight 2
    Assert-True (-not $artifactCheck.passed) 'A claimed but missing artifact must fail validation.'

    Assert-Throws { $null = Assert-HarnessInputs -ProjectRoot $root -State 'gameplay' -Resolution '320x200' -Output '..\outside.png' } 'Output traversal outside the project root must be rejected.'

    # Exercise suite aggregation through the public runner with a fake Godot
    # executable. A single failed member must make the whole suite fail.
    $fakeProject = Join-Path $root 'fake-project'
    [System.IO.Directory]::CreateDirectory((Join-Path $fakeProject 'tests/harness')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $fakeProject 'bin/Debug/net8.0')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $fakeProject '.harness')) | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tests/harness/scenarios.json') -Destination (Join-Path $fakeProject 'tests/harness/scenarios.json')
    & git -C $fakeProject init --quiet | Out-Null
    $assemblyPath = Join-Path $fakeProject 'bin/Debug/net8.0/kart_racer.dll'
    [System.IO.File]::WriteAllBytes($assemblyPath, [byte[]](1, 2, 3, 4))
    $sourceIdentity = Get-HarnessSourceIdentity -ProjectRoot $fakeProject
    $assemblyIdentity = Get-HarnessAssemblyIdentity -ProjectRoot $fakeProject
    [ordered]@{ schema_version = 1; source_fingerprint = $sourceIdentity.fingerprint; assembly_hash = $assemblyIdentity.hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fakeProject '.harness/build-receipt.json') -Encoding UTF8
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $fakeGodotBin = Join-Path $root 'fake-godot-bin'
    $fixtureProject = Join-Path $root 'FakeGodot.csproj'
    $fixtureSource = Join-Path $root 'FakeGodot.cs'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tests/fixtures/harness/FakeGodot.csproj.txt') -Destination $fixtureProject
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tests/fixtures/harness/FakeGodot.cs.txt') -Destination $fixtureSource
    $fakeBuild = & $dotnetCommand.Source build $fixtureProject -c Release --nologo -o $fakeGodotBin 2>&1
    Assert-Equal $LASTEXITCODE 0 'Native fake Godot fixture should compile for the current test platform.'
    $fakeGodot = if ($IsWindows) { Join-Path $fakeGodotBin 'FakeGodot.exe' } else { Join-Path $fakeGodotBin 'FakeGodot' }
    Assert-True (Test-Path -LiteralPath $fakeGodot -PathType Leaf) 'Native fake Godot executable should be present.'
    $env:FAKE_HARNESS_PWSH = $pwsh
    $env:FAKE_HARNESS_FIXTURE = $fixturePath
    $env:FAKE_HARNESS_FAIL_STATE = 'gameplay'
    $suiteThrew = $false
    try {
        Invoke-PainTaxiHarness -ProjectRoot $fakeProject -GodotPath $fakeGodot -DotnetPath $pwsh -Suite core -Resolution '320x200' -TimeoutSeconds 5 -SkipBuild -Crt $true -Scanlines $false 6>$null | Out-Null
    } catch { $suiteThrew = $true }
    Remove-Item Env:FAKE_HARNESS_FAIL_STATE -ErrorAction SilentlyContinue
    Remove-Item Env:FAKE_HARNESS_PWSH -ErrorAction SilentlyContinue
    Remove-Item Env:FAKE_HARNESS_FIXTURE -ErrorAction SilentlyContinue
    $manifests = @(Get-ChildItem -LiteralPath (Join-Path $fakeProject 'artifacts/harness') -Filter manifest.json -Recurse -File -ErrorAction SilentlyContinue)
    Assert-True $suiteThrew 'A suite with one failed member must return failure.'
    Assert-True ($manifests.Count -eq 1) 'A failed suite must still leave one reviewable manifest.'
    if ($manifests.Count -eq 1) {
        $manifest = Get-Content -Raw -LiteralPath $manifests[0].FullName | ConvertFrom-Json
        Assert-Equal $manifest.status 'failed' 'Suite manifest status should be failed when one member fails.'
        Assert-Equal @($manifest.scenarios).Count 8 'All eight requested core scenarios must run.'
        Assert-Equal @($manifest.scenarios | Where-Object status -eq 'passed').Count 7 'Only the injected member should fail.'
        Assert-Equal ((@($manifest.scenarios | Where-Object status -eq 'failed') | ForEach-Object state) -join ',') 'gameplay' 'The failed member must be the injected gameplay scenario.'
        Assert-True (@($manifest.scenarios | Where-Object status -eq 'failed').Count -ge 1) 'Suite manifest should identify the failed member.'
        $gameplayScenario = @($manifest.scenarios | Where-Object state -eq 'gameplay' | Select-Object -First 1)
        if ($gameplayScenario.Count -eq 1 -and $null -ne $gameplayScenario[0].observations.PSObject.Properties['crt']) {
            Assert-Equal $gameplayScenario[0].observations.crt $true 'Public runner should forward a true CRT option.'
            Assert-Equal $gameplayScenario[0].observations.scanlines $false 'Public runner should forward a false scanline option.'
        }
    }
}
finally {
    if ($KeepArtifacts -or $failures.Count -gt 0) { Write-Output "Contract artifacts: $root" }
    elseif (Test-Path -LiteralPath $root) {
        $resolved = [IO.Path]::GetFullPath($root)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^pain-taxi-harness-[a-f0-9]{32}$') { throw 'Refusing to delete a path outside the generated test directory.' }
        [System.IO.Directory]::Delete($resolved, $true)
    }
    Remove-Item Env:FAKE_HARNESS_MODE -ErrorAction SilentlyContinue
    Remove-Item Env:FAKE_HARNESS_FAIL_STATE -ErrorAction SilentlyContinue
    Remove-Item Env:FAKE_HARNESS_PWSH -ErrorAction SilentlyContinue
    Remove-Item Env:FAKE_HARNESS_FIXTURE -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Error ("Harness runner contract tests failed:`n - " + ($failures -join "`n - "))
    exit 1
}
Write-Output 'Harness runner contract tests passed.'
exit 0
