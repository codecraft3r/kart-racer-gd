param(
    [string]$InputDirectory = "$env:TEMP\suno-batch-02-all",
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"
$culture = [System.Globalization.CultureInfo]::InvariantCulture

$archiveRoot = Join-Path $ProjectRoot "audio_masters\suno_batch_02"
$originalRoot = Join-Path $archiveRoot "originals"
$masterRoot = Join-Path $archiveRoot "normalized_wav"
$gameRoot = Join-Path $ProjectRoot "assets\audio\music\game"

@($archiveRoot, $originalRoot, $masterRoot, $gameRoot) | ForEach-Object {
    New-Item -ItemType Directory -Force -Path $_ | Out-Null
}

# Mapping rationale (documented per file in the manifest notes):
# - Names match first (repair-shop->ChromeAndCoffee, shift-expired->MeterExpired,
#   cab-victory->PaidInFull, exact-change/dont-close-yet->radio songs).
# - impossible-fare matches the Last Fare Out brief phrase "one impossible fare".
# - final-meter-tick matches the Redline Receipt "accelerating taxi meter" tick.
# - neon-taxi-sprint measures 147 BPM against the Foundry Freeway 146 target.
# - Remaining pairs fill Wet Asphalt (loop) and Fare Evasion (boogie) by elimination;
#   Suno did not obey every BPM target, which the brief anticipates.
# - Within a pair, the base render is A and the "-2" render is B, except where noted.
$tracks = @(
    @{ Source = "neon-taxi-sprint-2.mp3";       Stem = "PTX_05_FoundryFreeway_A";    Title = "Foundry Freeway A";    Number = 5 },
    @{ Source = "neon-taxi-sprint.mp3";         Stem = "PTX_05_FoundryFreeway_B";    Title = "Foundry Freeway B";    Number = 5 },
    @{ Source = "neon-taxi-loop-2.mp3";         Stem = "PTX_06_WetAsphalt_A";        Title = "Wet Asphalt A";        Number = 6 },
    @{ Source = "neon-taxi-loop.mp3";           Stem = "PTX_06_WetAsphalt_B";        Title = "Wet Asphalt B";        Number = 6 },
    @{ Source = "pre-shift-boogie.mp3";         Stem = "PTX_07_FareEvasion_A";       Title = "Fare Evasion A";       Number = 7 },
    @{ Source = "pre-shift-boogie-2.mp3";       Stem = "PTX_07_FareEvasion_B";       Title = "Fare Evasion B";       Number = 7 },
    @{ Source = "glassy-hook-rubber-bass.mp3";  Stem = "PTX_08_RedlineReceipt_A";    Title = "Redline Receipt A";    Number = 8 },
    @{ Source = "final-meter-tick.mp3";         Stem = "PTX_08_RedlineReceipt_B";    Title = "Redline Receipt B";    Number = 8 },
    @{ Source = "impossible-fare-2.mp3";        Stem = "PTX_09_LastFareOut_A";       Title = "Last Fare Out A";      Number = 9 },
    @{ Source = "impossible-fare.mp3";          Stem = "PTX_09_LastFareOut_B";       Title = "Last Fare Out B";      Number = 9 },
    @{ Source = "neon-repair-shop.mp3";         Stem = "PTX_10_ChromeAndCoffee_A";   Title = "Chrome And Coffee A";  Number = 10 },
    @{ Source = "neon-repair-shop-2.mp3";       Stem = "PTX_10_ChromeAndCoffee_B";   Title = "Chrome And Coffee B";  Number = 10 },
    @{ Source = "neon-cab-victory-2.mp3";       Stem = "PTX_11_PaidInFull_A";        Title = "Paid In Full A";       Number = 11 },
    @{ Source = "neon-cab-victory.mp3";         Stem = "PTX_11_PaidInFull_B";        Title = "Paid In Full B";       Number = 11 },
    @{ Source = "shift-expired.mp3";            Stem = "PTX_12_MeterExpired_A";      Title = "Meter Expired A";      Number = 12 },
    @{ Source = "shift-expired-2.mp3";          Stem = "PTX_12_MeterExpired_B";      Title = "Meter Expired B";      Number = 12 },
    @{ Source = "exact-change-loose-plans.mp3"; Stem = "PTX_RADIO_15_ExactChange_A"; Title = "Exact Change A";       Number = 15 },
    @{ Source = "exact-change-loose-plans-2.mp3"; Stem = "PTX_RADIO_15_ExactChange_B"; Title = "Exact Change B";     Number = 15 },
    @{ Source = "dont-close-yet.mp3";           Stem = "PTX_RADIO_16_DontCloseYet_A"; Title = "Dont Close Yet A";    Number = 16 },
    @{ Source = "dont-close-yet-2.mp3";         Stem = "PTX_RADIO_16_DontCloseYet_B"; Title = "Dont Close Yet B";    Number = 16 }
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
    Write-Output ("Done " + $track.Stem)
}

$manifest | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $archiveRoot "music_manifest.csv")
Write-Output ("Processed " + $tracks.Count + " Suno renders.")
