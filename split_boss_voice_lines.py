#!/usr/bin/env python3
"""
Boss Voice Line Splitter
========================
Splits a long recording of boss voice lines into individual .wav clips
based on silence detection, then names and organizes them into Unity folders.

Dependencies:
    pip install pydub

Note: ffmpeg must be installed for .mp3 input support.
    macOS:   brew install ffmpeg
    Windows: choco install ffmpeg   (or download from https://ffmpeg.org)
    Linux:   sudo apt install ffmpeg

Usage:
    python split_boss_voice_lines.py path/to/recording.wav
    python split_boss_voice_lines.py path/to/recording.mp3 --silence-thresh -35 --min-silence 400
    python split_boss_voice_lines.py path/to/recording.wav --output-root ./Assets/Audio/BossVoice
    python split_boss_voice_lines.py path/to/recording.wav --dry-run
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path

try:
    from pydub import AudioSegment
    from pydub.silence import detect_nonsilent
except ImportError:
    print("ERROR: pydub is not installed. Run: pip install pydub")
    sys.exit(1)


# Voice lines in recording order, grouped by category.
# Each tuple is (category, line_text).
VOICE_LINES = [
    # Intro
    ("Intro",          "Behold the cube root of all evil"),
    ("Intro",          "I am not a box I am a problem with corners"),
    ("Intro",          "Six faces zero chill"),
    # Slam
    ("Slam",           "Bonk incoming"),
    ("Slam",           "Big bonk"),
    ("Slam",           "Newton said I could"),
    ("Slam",           "Get flattened"),
    # DelayedFakeout
    ("DelayedFakeout", "Wait for it"),
    ("DelayedFakeout", "Too early"),
    ("DelayedFakeout", "You dodged air impressive"),
    ("DelayedFakeout", "I was buffering the bonk"),
    # Roll
    ("Roll",           "Beep beep cube coming through"),
    ("Roll",           "No brakes"),
    ("Roll",           "Im on a roll"),
    ("Roll",           "Cube drift"),
    # Laser
    ("Laser",          "Say hello to my little face"),
    ("Laser",          "Beam time"),
    ("Laser",          "Face the consequences"),
    ("Laser",          "Laser tax"),
    # BigLaser
    ("BigLaser",       "Time to sweep the competition"),
    ("BigLaser",       "All faces online"),
    ("BigLaser",       "Six faces zero chill 2"),
    ("BigLaser",       "Cleanup on aisle you"),
    # Fakeout
    ("Fakeout",        "Charging laser just kidding"),
    ("Fakeout",        "Gotcha"),
    ("Fakeout",        "You thought slam I thought beam"),
    # BossHurt
    ("BossHurt",       "Ow my favorite face"),
    ("BossHurt",       "Warranty"),
    ("BossHurt",       "My warranty doesnt cover bullets"),
    # PlayerHit
    ("PlayerHit",      "You have been cubed"),
    ("PlayerHit",      "Too slow"),
    ("PlayerHit",      "Geometry lesson complete"),
    # PhaseTwo
    ("PhaseTwo",       "Youve activated my villain arc it has corners"),
    ("PhaseTwo",       "Phase two"),
    ("PhaseTwo",       "I was only using five faces the sixth one is personal"),
    # Death
    ("Death",          "Tell my corners I loved them"),
    ("Death",          "Cube down"),
    ("Death",          "Remember me as more than a box"),
]

EXPECTED_COUNT = len(VOICE_LINES)  # 38


def sanitize_filename(text: str) -> str:
    """Convert a voice line into a safe, readable filename fragment."""
    text = text.lower()
    text = re.sub(r"[^a-z0-9 ]", "", text)
    text = re.sub(r"\s+", "_", text.strip())
    if len(text) > 50:
        text = text[:50].rstrip("_")
    return text


def build_filename(category: str, index_in_category: int, line_text: str) -> str:
    """Build a filename like Slam_02_big_bonk.wav"""
    fragment = sanitize_filename(line_text)
    return f"{category}_{index_in_category:02d}_{fragment}.wav"


def split_audio(input_path: str, silence_thresh: int, min_silence_len: int,
                output_root: str, dry_run: bool, pad_ms: int) -> dict:
    """
    Main splitting logic.
    Returns a report dict.
    """
    input_path = os.path.abspath(input_path)
    if not os.path.isfile(input_path):
        print(f"ERROR: Input file not found: {input_path}")
        sys.exit(1)

    ext = os.path.splitext(input_path)[1].lower()
    print(f"Loading {input_path} ...")
    if ext == ".mp3":
        audio = AudioSegment.from_mp3(input_path)
    elif ext in (".wav", ".wave"):
        audio = AudioSegment.from_wav(input_path)
    elif ext == ".ogg":
        audio = AudioSegment.from_ogg(input_path)
    else:
        audio = AudioSegment.from_file(input_path)

    print(f"  Duration: {len(audio) / 1000:.2f}s  Channels: {audio.channels}  "
          f"Sample rate: {audio.frame_rate}  Sample width: {audio.sample_width * 8}bit")

    print(f"Detecting non-silent chunks (threshold={silence_thresh} dBFS, "
          f"min_silence={min_silence_len}ms) ...")
    chunks = detect_nonsilent(audio, min_silence_len=min_silence_len,
                              silence_thresh=silence_thresh, seek_step=5)
    detected_count = len(chunks)
    print(f"  Detected {detected_count} chunks (expected {EXPECTED_COUNT})")

    report = {
        "input_file": input_path,
        "total_chunks_detected": detected_count,
        "expected_line_count": EXPECTED_COUNT,
        "match": detected_count == EXPECTED_COUNT,
        "warnings": [],
        "exported_files": [],
    }

    if detected_count != EXPECTED_COUNT:
        warning = (f"MISMATCH: Detected {detected_count} chunks but expected "
                   f"{EXPECTED_COUNT}. Adjust --silence-thresh or --min-silence.")
        report["warnings"].append(warning)
        print(f"  WARNING: {warning}")
        if detected_count < EXPECTED_COUNT:
            hint = ("  HINT: Try a lower silence threshold (e.g. -45) or a "
                    "shorter min silence length (e.g. 300).")
            print(hint)
        else:
            hint = ("  HINT: Try a higher silence threshold (e.g. -30) or a "
                    "longer min silence length (e.g. 600).")
            print(hint)

    category_counters: dict[str, int] = {}
    export_count = min(detected_count, EXPECTED_COUNT)

    for i in range(export_count):
        start_ms, end_ms = chunks[i]
        category, line_text = VOICE_LINES[i]

        cat_idx = category_counters.get(category, 0) + 1
        category_counters[category] = cat_idx

        filename = build_filename(category, cat_idx, line_text)
        category_dir = os.path.join(output_root, category)
        filepath = os.path.join(category_dir, filename)

        padded_start = max(0, start_ms - pad_ms)
        padded_end = min(len(audio), end_ms + pad_ms)
        chunk_audio = audio[padded_start:padded_end]

        entry = {
            "line_number": i + 1,
            "category": category,
            "line_text": line_text,
            "filename": filename,
            "path": filepath,
            "start_ms": start_ms,
            "end_ms": end_ms,
            "duration_ms": end_ms - start_ms,
        }
        report["exported_files"].append(entry)

        if dry_run:
            print(f"  [DRY RUN] #{i+1:02d} {category}/{filename}  "
                  f"({start_ms}ms - {end_ms}ms, {end_ms - start_ms}ms)")
        else:
            os.makedirs(category_dir, exist_ok=True)
            chunk_audio.export(filepath, format="wav")
            print(f"  Exported #{i+1:02d} {category}/{filename}  "
                  f"({(end_ms - start_ms) / 1000:.2f}s)")

    if detected_count > EXPECTED_COUNT:
        for i in range(EXPECTED_COUNT, detected_count):
            start_ms, end_ms = chunks[i]
            warning = (f"Extra chunk #{i+1} at {start_ms}ms-{end_ms}ms "
                       f"({(end_ms - start_ms) / 1000:.2f}s) - not exported")
            report["warnings"].append(warning)
            print(f"  WARNING: {warning}")

    return report


def main():
    parser = argparse.ArgumentParser(
        description="Split boss voice line recordings into individual clips")
    parser.add_argument("input", help="Path to the input audio file (.wav or .mp3)")
    parser.add_argument("--silence-thresh", type=int, default=-40,
                        help="Silence threshold in dBFS (default: -40). "
                             "Lower = more sensitive to quiet sounds.")
    parser.add_argument("--min-silence", type=int, default=500,
                        help="Minimum silence gap between lines in ms (default: 500)")
    parser.add_argument("--output-root", type=str,
                        default="Assets/Audio/BossVoice",
                        help="Root output directory (default: Assets/Audio/BossVoice)")
    parser.add_argument("--pad-ms", type=int, default=50,
                        help="Padding in ms added before/after each clip (default: 50)")
    parser.add_argument("--dry-run", action="store_true",
                        help="Show what would be exported without writing files")
    parser.add_argument("--report", type=str, default="voice_line_split_report.json",
                        help="Path for the JSON report (default: voice_line_split_report.json)")

    args = parser.parse_args()

    report = split_audio(
        input_path=args.input,
        silence_thresh=args.silence_thresh,
        min_silence_len=args.min_silence,
        output_root=args.output_root,
        dry_run=args.dry_run,
        pad_ms=args.pad_ms,
    )

    report_path = args.report
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"\nReport written to: {report_path}")

    if report["match"]:
        print(f"\nSUCCESS: All {EXPECTED_COUNT} voice lines matched and exported.")
    else:
        print(f"\nWARNING: Chunk count ({report['total_chunks_detected']}) != "
              f"expected ({EXPECTED_COUNT}). Review the report and adjust parameters.")

    return 0 if report["match"] else 1


if __name__ == "__main__":
    sys.exit(main())
