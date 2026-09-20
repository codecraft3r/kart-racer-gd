<#
    Small, renderer-free process used by harness contract tests.

    The process intentionally speaks the same result shape as the Godot
    harness, but it does not load a project or depend on a display server.
    Keep its behaviours boring and explicit: the supervisor tests should be
    able to prove timeout, diagnostics, freshness, and result validation.
#>
$Remaining = @($args)

$options = @{}
for ($i = 0; $i -lt @($Remaining).Count; $i++) {
    $token = [string]$Remaining[$i]
    if (-not $token.StartsWith('-')) { continue }

    $key = $token.TrimStart('-')
    $value = $true
    if ($key -match '^([^=]+)=(.*)$') {
        $key = $Matches[1]
        $value = $Matches[2]
    }
    if ($i + 1 -lt @($Remaining).Count) {
        $candidate = [string]$Remaining[$i + 1]
        if ($value -eq $true -and -not $candidate.StartsWith('-')) {
            $value = $candidate
            $i++
        }
    }
    $options[$key.ToLowerInvariant().Replace('-', '')] = $value
}

if (@($Remaining | ForEach-Object { [string]$_ }) -contains '--version') {
    Write-Output 'Godot Engine v4.6.3.stable.mono'
    exit 0
}
if (@($Remaining | ForEach-Object { [string]$_ }) -contains '--import') {
    Write-Output 'fixture import completed'
    exit 0
}

function Get-Option {
    param([string]$Name, [object]$Default = $null)
    $key = $Name.ToLowerInvariant().Replace('-', '')
    if ($options.ContainsKey($key)) { return $options[$key] }
    return $Default
}

$script:Crc32Table = [int64[]]::new(256)
for ($tableIndex = 0; $tableIndex -lt 256; $tableIndex++) {
    [int64]$tableValue = $tableIndex
    for ($tableBit = 0; $tableBit -lt 8; $tableBit++) {
        if (($tableValue -band 1) -ne 0) { $tableValue = ($tableValue -shr 1) -bxor 3988292384 }
        else { $tableValue = $tableValue -shr 1 }
    }
    $script:Crc32Table[$tableIndex] = $tableValue
}

function Get-Crc32 {
    param([byte[]]$Bytes)
    # Keep the accumulator in Int64 so PowerShell does not coerce the
    # high-bit CRC values to a signed Int32 during -bxor.
    [int64]$crc = 4294967295
    foreach ($byte in $Bytes) {
        $tableIndex = [int](($crc -bxor [int64]$byte) -band 255)
        $crc = ($crc -shr 8) -bxor $script:Crc32Table[$tableIndex]
    }
    return [uint32]($crc -bxor 4294967295)
}

function Add-PngUInt32 {
    param([System.Collections.Generic.List[byte]]$Target, [uint32]$Value)
    [void]$Target.Add([byte](($Value -shr 24) -band 0xff))
    [void]$Target.Add([byte](($Value -shr 16) -band 0xff))
    [void]$Target.Add([byte](($Value -shr 8) -band 0xff))
    [void]$Target.Add([byte]($Value -band 0xff))
}

function Add-PngChunk {
    param([System.Collections.Generic.List[byte]]$Target, [string]$Type, [byte[]]$Payload)
    $typeBytes = [System.Text.Encoding]::ASCII.GetBytes($Type)
    Add-PngUInt32 -Target $Target -Value ([uint32]$Payload.Length)
    foreach ($byte in $typeBytes) { [void]$Target.Add($byte) }
    foreach ($byte in $Payload) { [void]$Target.Add($byte) }
    $crcBytes = [byte[]]($typeBytes + $Payload)
    Add-PngUInt32 -Target $Target -Value (Get-Crc32 -Bytes $crcBytes)
}

function New-PngBytes {
    param([int]$Width, [int]$Height)
    # Transparent RGBA scanlines with PNG filter 0. Allocate in one operation
    # so the process timeout measures supervision, not interpreted pixel loops.
    $raw = [byte[]]::new(($Width * 4 + 1) * $Height)
    $compressedStream = [System.IO.MemoryStream]::new()
    $deflate = [System.IO.Compression.ZLibStream]::new($compressedStream, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $deflate.Write($raw, 0, $raw.Length)
    $deflate.Dispose()
    $header = [System.Collections.Generic.List[byte]]::new()
    foreach ($byte in [byte[]](137, 80, 78, 71, 13, 10, 26, 10)) { [void]$header.Add($byte) }
    $ihdr = [System.Collections.Generic.List[byte]]::new()
    Add-PngUInt32 -Target $ihdr -Value ([uint32]$Width)
    Add-PngUInt32 -Target $ihdr -Value ([uint32]$Height)
    foreach ($byte in [byte[]](8, 6, 0, 0, 0)) { [void]$ihdr.Add($byte) }
    Add-PngChunk -Target $header -Type 'IHDR' -Payload $ihdr.ToArray()
    Add-PngChunk -Target $header -Type 'IDAT' -Payload $compressedStream.ToArray()
    Add-PngChunk -Target $header -Type 'IEND' -Payload ([byte[]]@())
    $compressedStream.Dispose()
    return $header.ToArray()
}

$mode = [string](Get-Option 'Mode' $env:FAKE_HARNESS_MODE)
if ([string]::IsNullOrWhiteSpace($mode)) { $mode = 'pass' }
$outputDir = [string](Get-Option 'OutputDir' (Get-Option 'Output' '.'))
$runId = [string](Get-Option 'RunId' 'fixture-run')
$state = [string](Get-Option 'State' 'gameplay')
$resultName = [string](Get-Option 'ResultName' 'result.json')
$artifactName = [string](Get-Option 'ArtifactName' 'capture.png')
$sleepSeconds = [int](Get-Option 'SleepSeconds' 20)
$exitCode = [int](Get-Option 'ExitCode' 0)
$crt = [string](Get-Option 'Crt' 'false')
$scanlines = [string](Get-Option 'Scanlines' 'false')
$seed = [int](Get-Option 'Seed' 1337)
$setupMode = [string](Get-Option 'Setup-Mode' 'fixture')
$resolution = [string](Get-Option 'Resolution' '2x2')
$width = 2
$height = 2
if ($resolution -match '^([1-9][0-9]{1,5})x([1-9][0-9]{1,5})$') {
    $width = [int]$Matches[1]
    $height = [int]$Matches[2]
}
$failedState = [string]$env:FAKE_HARNESS_FAIL_STATE
if (-not [string]::IsNullOrWhiteSpace($failedState) -and $state -eq $failedState) { $mode = 'suite-fail' }
$requestedOutput = if (-not [string]::IsNullOrWhiteSpace($env:FAKE_HARNESS_OUTPUT)) { [string]$env:FAKE_HARNESS_OUTPUT } else { [string](Get-Option 'Output') }
$requestedResult = if (-not [string]::IsNullOrWhiteSpace($env:FAKE_HARNESS_RESULT)) { [string]$env:FAKE_HARNESS_RESULT } else { [string](Get-Option 'Result') }
if (-not [string]::IsNullOrWhiteSpace($requestedOutput)) {
    $outputDir = Split-Path -Parent $requestedOutput
    $artifactName = Split-Path -Leaf $requestedOutput
}
$resultPathOverride = $null
if (-not [string]::IsNullOrWhiteSpace($requestedResult)) { $resultPathOverride = $requestedResult }

if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Output "fixture mode=$mode state=$state run_id=$runId crt=$crt scanlines=$scanlines"
if ($mode -eq 'diagnostic-error') {
    [Console]::Error.WriteLine('SCRIPT ERROR: fixture emitted a diagnostic despite exit code zero')
}
if ($mode -eq 'timeout') {
    Start-Sleep -Seconds $sleepSeconds
    exit 0
}

$resultPath = if ($null -ne $resultPathOverride) { $resultPathOverride } else { Join-Path $outputDir $resultName }
$artifactPath = Join-Path $outputDir $artifactName
$status = 'passed'
$resultState = $state
$errors = @()
$assertions = @([ordered]@{ name = 'fixture_completed'; passed = $true; observed = $state })
$artifacts = @()

switch ($mode) {
    'invalid-state' {
        $resultState = 'menu'
        $errors = @('requested state was not observed')
        $assertions = @([ordered]@{ name = 'requested_state'; passed = $false; observed = $resultState })
        $status = 'failed'
    }
    'suite-fail' {
        $errors = @('suite member deliberately failed')
        $assertions = @([ordered]@{ name = 'suite_member'; passed = $false; observed = $state })
        $status = 'failed'
    }
    'missing-artifact' {
        $artifacts = @([ordered]@{ kind = 'capture'; path = $artifactPath })
    }
    'stale-output' {
        # Leave an existing result untouched. The supervisor must reject it
        # when the current process did not create a fresh record.
        if (Test-Path -LiteralPath $resultPath) { exit 0 }
    }
    'missing-result' {
        [System.IO.File]::WriteAllBytes($artifactPath, (New-PngBytes -Width $width -Height $height))
        exit $exitCode
    }
    'invalid-contract' {
        Set-Content -LiteralPath $resultPath -Value '{"schema_version":1,"status":"passed"}' -Encoding utf8
        exit $exitCode
    }
}

if ($mode -ne 'missing-artifact') {
    [System.IO.File]::WriteAllBytes($artifactPath, (New-PngBytes -Width $width -Height $height))
    $artifacts = @([ordered]@{ kind = 'capture'; path = $artifactPath })
}

$result = [ordered]@{
    schema_version = 1
    seed = $seed
    setup_mode = $setupMode
    resolution = [ordered]@{ width = $width; height = $height }
    run_id = $runId
    state = $resultState
    status = $status
    errors = @($errors)
    assertions = @($assertions)
    artifacts = @($artifacts)
    observations = [ordered]@{ fixture = $true; seed = $seed; setup_mode = $setupMode; resolution = $resolution; crt = ($crt -eq 'true'); scanlines = ($scanlines -eq 'true') }
    cleanup_complete = $true
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding utf8
exit $exitCode
