param(
    [string]$InputDirectory = "$env:TEMP\pain_taxi_suno_batch_01",
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$culture = [System.Globalization.CultureInfo]::InvariantCulture

$archiveRoot = Join-Path $ProjectRoot "audio_masters\suno_batch_01"
$originalRoot = Join-Path $archiveRoot "originals"
$masterRoot = Join-Path $archiveRoot "normalized_wav"
$gameRoot = Join-Path $ProjectRoot "assets\audio\music\game"

@($archiveRoot, $originalRoot, $masterRoot, $gameRoot) | ForEach-Object {
    New-Item -ItemType Directory -Force -Path $_ | Out-Null
}

$tracks = @(
    @{ Source = "Neon Taxi Meter_2.mp3";      Stem = "PTX_01_MeterGlow_A";        Title = "Meter Glow A";          Number = 1 },
    @{ Source = "Neon Taxi Meter_3.mp3";      Stem = "PTX_01_MeterGlow_B";        Title = "Meter Glow B";          Number = 1 },
    @{ Source = "Neon Taxi Meter.mp3";        Stem = "PTX_02_DispatchAfterDark_A"; Title = "Dispatch After Dark A"; Number = 2 },
    @{ Source = "Neon Taxi Meter_1.mp3";      Stem = "PTX_02_DispatchAfterDark_B"; Title = "Dispatch After Dark B"; Number = 2 },
    @{ Source = "Neon Taxi Match.mp3";        Stem = "PTX_03_FlagfallFever_A";     Title = "Flagfall Fever A";      Number = 3 },
    @{ Source = "Neon Taxi Match_1.mp3";      Stem = "PTX_03_FlagfallFever_B";     Title = "Flagfall Fever B";      Number = 3 },
    @{ Source = "Midnight Taxi Meter.mp3";    Stem = "PTX_04_RushHourRiot_A";      Title = "Rush Hour Riot A";       Number = 4 },
    @{ Source = "Midnight Taxi Meter_1.mp3";  Stem = "PTX_04_RushHourRiot_B";      Title = "Rush Hour Riot B";       Number = 4 }
)

function Invoke-CheckedFfmpeg {
    param([string[]]$Arguments)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & ffmpeg @Arguments
    $ErrorActionPreference = $previousPreference
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed with exit code $LASTEXITCODE"
    }
}

function Get-LoudnormMeasurement {
    param([string]$InputPath)

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = (& ffmpeg -hide_banner -nostats -i $InputPath -af "loudnorm=I=-16:TP=-1.5:LRA=9:print_format=json" -f null NUL 2>&1 | Out-String)
    $ErrorActionPreference = $previousPreference
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg loudness analysis failed for $InputPath"
    }

    $match = [regex]::Match($output, '(?s)\{\s*"input_i".*?\}')
    if (!$match.Success) {
        throw "Could not parse loudness analysis for $InputPath"
    }

    return $match.Value | ConvertFrom-Json
}

$manifest = @()

foreach ($track in $tracks) {
    $sourcePath = Join-Path $InputDirectory $track.Source
    if (!(Test-Path -LiteralPath $sourcePath)) {
        throw "Missing Suno render: $sourcePath"
    }

    $originalPath = Join-Path $originalRoot "$($track.Stem)_source.mp3"
    $masterPath = Join-Path $masterRoot "$($track.Stem)_master.wav"
    $gamePath = Join-Path $gameRoot "$($track.Stem).ogg"

    Copy-Item -LiteralPath $sourcePath -Destination $originalPath -Force

    $measurement = Get-LoudnormMeasurement -InputPath $sourcePath
    $filter = "loudnorm=I=-16:TP=-1.5:LRA=9:" +
        "measured_I=$($measurement.input_i):" +
        "measured_TP=$($measurement.input_tp):" +
        "measured_LRA=$($measurement.input_lra):" +
        "measured_thresh=$($measurement.input_thresh):" +
        "offset=$($measurement.target_offset):" +
        "linear=true:print_format=summary"

    Invoke-CheckedFfmpeg -Arguments @(
        "-y", "-hide_banner", "-loglevel", "warning",
        "-i", $sourcePath,
        "-af", $filter,
        "-ar", "48000", "-ac", "2",
        "-c:a", "pcm_s24le",
        "-metadata", "title=$($track.Title)",
        "-metadata", "album=PAIN TAXI",
        "-metadata", "track=$($track.Number)",
        $masterPath
    )

    Invoke-CheckedFfmpeg -Arguments @(
        "-y", "-hide_banner", "-loglevel", "warning",
        "-i", $masterPath,
        "-c:a", "libvorbis", "-q:a", "6",
        "-ar", "48000", "-ac", "2",
        "-metadata", "title=$($track.Title)",
        "-metadata", "album=PAIN TAXI",
        "-metadata", "track=$($track.Number)",
        $gamePath
    )

    $durationText = & ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $gamePath
    $duration = [double]::Parse($durationText.Trim(), $culture)
    $manifest += [pscustomobject]@{
        stem = $track.Stem
        title = $track.Title
        source_file = $track.Source
        source_format = "Suno MP3"
        archive_master = (Resolve-Path $masterPath).Path.Substring($ProjectRoot.Length + 1).Replace('\', '/')
        game_asset = (Resolve-Path $gamePath).Path.Substring($ProjectRoot.Length + 1).Replace('\', '/')
        duration_seconds = [math]::Round($duration, 3)
        target_lufs = -16
        true_peak_ceiling_dbtp = -1.5
        game_codec = "Vorbis q6 / 48 kHz stereo"
    }
}

$manifest | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $archiveRoot "music_manifest.csv")
Write-Output "Processed $($tracks.Count) Suno renders."
