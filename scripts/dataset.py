"""
dataset.py

Dataset-specific loader for SpectroVis Wireshark JSON exports.
Keeps all path conventions and mode aliases in one place.
"""

from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path
from typing import List

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent

LOAD_DIR = PROJECT_ROOT / "docs" / "wireshark" / "spectrovis" / "workflow_1" / "json_files"
OUTPUT_DIR = PROJECT_ROOT / "docs" / "protocol" / "workflow_1"

ALIAS = {
    "405nm": "fluorescence_405nm",
    "500nm": "fluorescence_500nm",
    "raw": "raw_counts",
    "time": "time_resolved",
    "full": "full_spectrum",
    "event": "event_triggered",
}

IN = "in"
OUT = "out"


def alias(mode: str) -> str:
    """Normalize shorthand mode names to canonical dataset directory names."""
    return ALIAS.get(mode, mode)


def run_pattern(direction: str) -> str:
    if direction not in (IN, OUT):
        raise ValueError(f"Invalid direction: {direction}")
    return f"{direction}_*.json"


def list_runs(path: Path, pattern) -> List[Path]:
    """List and sort log files in a directory; fail loudly if empty."""
    files = sorted(path.glob(pattern))
    if not files:
        raise FileNotFoundError(
            f"No log files found in dir: {path} (pattern={pattern})"
        )
    return files


def parse_time(s: str) -> float:
    """Parse Wireshark ISO time string to Unix seconds (microsecond precision)."""
    s = s.replace("Z", "")
    if "." in s:
        base, frac = s.split(".")
        frac = (frac + "000000")[:6]
        s = f"{base}.{frac}"
    dt = datetime.fromisoformat(s)
    return dt.timestamp()


def load_data(path: Path) -> List[dict]:
    """
    Load a Wireshark-exported JSON file and extract relevant USB/HID fields.

    Adds per-log relative timestamp (ms) based on first packet.
    """
    data = json.loads(path.read_text(encoding="utf-8"))
    packets: List[dict] = []

    for item in data:
        layers = item["_source"]["layers"]
        frame = layers.get("frame", {})
        usb = layers.get("usb", {})
        payload = layers.get("usbhid.data")

        time = frame.get("frame.time")
        time_sec = parse_time(time) if time else None

        packets.append(
            {
                "time_sec": time_sec,
                "frame_no": int(frame.get("frame.number", -1)),
                "endpoint": usb.get("usb.endpoint_address"),
                "data_len": int(usb.get("usb.data_len", "0")),
                "payload": payload,  # colon-separated hex
            }
        )

    if packets and packets[0]["time_sec"] is not None:
        t0 = packets[0]["time_sec"]
        for p in packets:
            p["time_rel_ms"] = (p["time_sec"] - t0) * 1000.0
    else:
        for p in packets:
            p["time_rel_ms"] = None

    return packets


def extract_hid_payloads(packets: List[dict]) -> List[str]:
    """Extract payload strings, preserving order, dropping packets without payload."""
    return [p["payload"] for p in packets if p.get("payload")]


def load_payload_runs(log_files: List[Path]) -> List[List[str]]:
    """Load multiple logs and return payload sequences (one list per capture)."""
    return [extract_hid_payloads(load_data(p)) for p in log_files]


def load_init(direction: str = OUT) -> List[Path]:
    """Load all capture files for the initialization sequence."""
    path = LOAD_DIR / "init"
    return list_runs(path, run_pattern(direction))


def load_change_mode(end: str, start: str, direction: str = OUT) -> List[Path]:
    """
    Load all capture files for a directed operating-mode transition.

    'start' is the previous mode, 'end' is the resulting mode.
    Direction matters and is preserved.
    """
    end = alias(end)
    start = alias(start)
    path = LOAD_DIR / "change_mode" / end / start
    return list_runs(path, run_pattern(direction))


def load_change_acquisition(
    mode: str, end: str, start: str, direction: str = OUT
) -> List[Path]:
    """
    Load all capture files for a directed acquisition-mode transition.

    Folder layout:
      change_acquisition/<mode>/<end>/<start>/out_*.json

    - 'mode'  : operating mode (absorbance, transmittance, intensity, ...)
    - 'start' : previous acquisition state (full, init, event, ...)
    - 'end'   : resulting acquisition state (time_resolved, full, ...)
    """
    mode = alias(mode)
    end = alias(end)
    start = alias(start)

    path = LOAD_DIR / "change_acquisition" / mode / end / start
    return list_runs(path, run_pattern(direction))


def load_calibration(mode: str, direction: str = OUT) -> List[Path]:
    path = LOAD_DIR / "calibration" / mode
    return list_runs(path, run_pattern(direction))


def load_experiment(mode: str, acq: str, direction: str = OUT) -> List[Path]:
    mode = alias(mode)
    acq = alias(acq)
    path = LOAD_DIR / "set_experiment" / mode / acq
    return list_runs(path, run_pattern(direction))


def load_measurement(mode: str, acq: str, direction: str = OUT) -> List[Path]:
    mode = alias(mode)
    acq = alias(acq)
    path = LOAD_DIR / "measurement" / mode / acq
    return list_runs(path, run_pattern(direction))


def load_acquisition_params(mode: str, acq: str, direction: str = OUT) -> List[Path]:
    mode = alias(mode)
    acq = alias(acq)
    path = LOAD_DIR / "acquisition_param" / mode / acq
    return list_runs(path, run_pattern(direction))


def load_recalibration(mode: str, acq: str, direction: str = OUT) -> List[Path]:
    mode = alias(mode)
    acq = alias(acq)
    path = LOAD_DIR / "recalibration" / mode / acq
    return list_runs(path, run_pattern(direction))


def load_close(direction: str = OUT) -> List[Path]:
    path = LOAD_DIR / "close"
    return list_runs(path, run_pattern(direction))


def output_path_init() -> Path:
    return OUTPUT_DIR / "trace_mask" / "init.txt"


def output_path_mode_change(end: str, start: str) -> Path:
    return OUTPUT_DIR / "trace_mask" / f"change_{end}_from_{start}.txt"


def output_path_diff(a_end: str, a_start: str, b_end: str, b_start: str) -> Path:
    name = f"diff_{a_end}_from_{a_start}__vs__{b_end}_from_{b_start}.txt"
    return OUTPUT_DIR / "trace_diff" / name


def output_path_acq_change(mode: str, end: str, start: str) -> Path:
    mode = alias(mode)
    end = alias(end)
    start = alias(start)
    return OUTPUT_DIR / "trace_mask" / f"acq_{mode}_{end}_from_{start}.txt"


def output_path_calibration(mode: str) -> Path:
    return OUTPUT_DIR / "trace_mask" / f"calibration_{mode}.txt"


def output_path_experiment(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_mask" / f"experiment_{mode}_{acq}.txt"


def output_path_measurement(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_mask" / f"measurement_{mode}_{acq}.txt"


def output_path_acquisition_params(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_mask" / f"acq_param_{mode}_{acq}.txt"


def output_path_recalibration(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_mask" / f"recal_{mode}_{acq}.txt"


def output_path_close() -> Path:
    return OUTPUT_DIR / "trace_mask" / "close.txt"


def output_path_init_dump() -> Path:
    return OUTPUT_DIR / "trace_dump" / "init.txt"


def output_path_mode_change_dump(end: str, start: str) -> Path:
    return OUTPUT_DIR / "trace_dump" / f"change_{end}_from_{start}.txt"


def output_path_acq_change_dump(mode: str, end: str, start: str) -> Path:
    mode = alias(mode)
    end = alias(end)
    start = alias(start)
    return OUTPUT_DIR / "trace_dump" / f"acq_{mode}_{end}_from_{start}.txt"


def output_path_calibration_dump(mode: str) -> Path:
    return OUTPUT_DIR / "trace_dump" / f"calibration_{mode}.txt"


def output_path_experiment_dump(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_dump" / f"experiment_{mode}_{acq}.txt"


def output_path_measurement_dump(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_dump" / f"measurement_{mode}_{acq}.txt"


def output_path_acq_params_dump(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_dump" / f"acq_param_{mode}_{acq}.txt"


def output_path_recal_dump(mode: str, acq: str) -> Path:
    return OUTPUT_DIR / "trace_dump" / f"recal_{mode}_{acq}.txt"


def output_path_close_dump() -> Path:
    return OUTPUT_DIR / "trace_dump" / "close.txt"


def write_output(text: str, path: Path) -> None:
    """Write UTF-8 text to a file, creating parent directories if needed."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
