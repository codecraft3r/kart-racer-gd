#!/usr/bin/env python3
"""Regenerate audio_masters/audio_loop_loudness.csv.

Measures every shipped asset under assets/audio/** with ffprobe/ffmpeg and
records duration, format, EBU R128 integrated loudness, true peak, and the
loop mode the runtime applies. Deterministic: same inputs -> same CSV.

Usage:  python tools/measure_audio_loop_loudness.py
"""
from __future__ import annotations

import csv
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
AUDIO_ROOT = ROOT / "assets" / "audio"
OUT = ROOT / "audio_masters" / "audio_loop_loudness.csv"
EXTS = {".ogg", ".wav"}

# Loop mode is applied at runtime by audio/AudioManager.cs (LoadLoop / PlayMusic)
# and audio/VehicleAudioController.cs; source files are never re-encoded to bake
# loop points in.
LOOP_RULES = [
    ("music/game/", "runtime_loop_forward",
     "AudioManager.PlayMusic sets AudioStreamOggVorbis.Loop=true and crossfades 1.2 s between MusicA/MusicB; no baked loop point."),
    ("ambience/", "runtime_loop_forward",
     "AudioManager.LoadLoop sets Loop=true for the ambience bed; bed material is continuous so wrap-around is inaudible."),
    ("vehicles/engine_idle", "runtime_loop_forward",
     "VehicleAudioController idle layer; source is an already loop-cut engine cycle, LoopMode=Forward over the whole file."),
    ("vehicles/engine_drive", "runtime_loop_forward",
     "VehicleAudioController drive layer; source is an already loop-cut engine cycle, LoopMode=Forward over the whole file."),
    ("vehicles/tire_skid", "runtime_loop_forward",
     "Skid layer looped while lateral slip is high; volume-driven, not retriggered."),
]


def loop_info(rel: str) -> tuple[str, str]:
    for key, mode, note in LOOP_RULES:
        if key in rel:
            return mode, note
    return "one_shot", "Fired as a pooled one-shot; no loop flag applied."


def probe(path: Path) -> dict:
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "a:0",
         "-show_entries", "stream=sample_rate,channels,codec_name",
         "-show_entries", "format=duration", "-of", "json", str(path)],
        capture_output=True, text=True, check=True).stdout
    data = json.loads(out)
    stream = data["streams"][0]
    return {
        "duration": round(float(data["format"]["duration"]), 3),
        "sample_rate": stream["sample_rate"],
        "channels": stream["channels"],
        "codec": stream["codec_name"],
    }


def loudness(path: Path) -> tuple[str, str]:
    res = subprocess.run(
        ["ffmpeg", "-hide_banner", "-nostats", "-i", str(path),
         "-af", "loudnorm=I=-16:TP=-1.5:print_format=json", "-f", "null", "-"],
        capture_output=True, text=True)
    match = re.search(r'\{\s*"input_i".*?\}', res.stderr, re.S)
    if not match:
        return "", ""
    blob = json.loads(match.group(0))
    return blob["input_i"], blob["input_tp"]


def main() -> int:
    rows = []
    files = sorted(p for p in AUDIO_ROOT.rglob("*") if p.suffix.lower() in EXTS)
    for path in files:
        rel = path.relative_to(ROOT).as_posix()
        info = probe(path)
        lufs, tp = loudness(path)
        mode, note = loop_info(rel)
        rows.append({
            "local_path": rel,
            "duration_seconds": info["duration"],
            "sample_rate": info["sample_rate"],
            "channels": info["channels"],
            "codec": info["codec"],
            "integrated_lufs": lufs,
            "true_peak_dbtp": tp,
            "loop_mode": mode,
            "loop_notes": note,
        })
    with OUT.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()), lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    print(f"Wrote {len(rows)} rows to {OUT.relative_to(ROOT).as_posix()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
