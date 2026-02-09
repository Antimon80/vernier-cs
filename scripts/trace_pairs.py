"""
trace_pairs.py

Export deterministic OUT->IN windows as a text artifact for protocol RE.

Rule:
- Each OUT starts a segment.
- All subsequent IN packets belong to that OUT until the next OUT.
"""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import Any, Dict, List, Tuple

from dataset import (
    LOAD_DIR,
    OUTPUT_DIR,
    IN,
    OUT,
    alias,
    list_runs,
    load_data,
    write_output,
)
from protocol_tools import hex_payload, format_groups


# ---------- rendering helpers ----------

def _report_id(payload: str) -> str:
    b = hex_payload(payload)
    return f"{b[0]:02x}" if b else "??"


def _render_payload(payload: str, indent: str = "") -> str:
    bs = hex_payload(payload)
    toks = [f"{x:02x}" for x in bs]
    block = format_groups(toks, width=32)
    return "\n".join(indent + line for line in block.splitlines())


def render_segments(segments: List[Tuple[str, List[str]]]) -> str:
    lines: List[str] = []
    for i, (out_payload, resp) in enumerate(segments):
        if out_payload:
            lines.append(f"OUT #{i}: report={_report_id(out_payload)} len={len(hex_payload(out_payload))}")
            lines.append(_render_payload(out_payload))
        else:
            # Should rarely happen; kept explicit so you see it.
            lines.append(f"OUT #{i}: (missing)")

        if not resp:
            lines.append("  IN: (none)")
        else:
            for k, p in enumerate(resp):
                lines.append(f"  IN[{k}]: report={_report_id(p)} len={len(hex_payload(p))}")
                lines.append(_render_payload(p, indent="  "))

        lines.append("\n" + "-" * 72 + "\n")

    return "\n".join(lines).rstrip() + "\n"


# ---------- core logic ----------

def _sort_key(p: Dict[str, Any]) -> Tuple[int, float]:
    """
    Deterministic ordering:
    - Prefer global capture order via frame_no (Wireshark frame.number).
    - Fall back to time_sec if frame_no missing.
    """
    frame_no = int(p.get("frame_no", -1))
    time_sec = p.get("time_sec")
    if time_sec is None:
        time_sec = float("inf")
    return (frame_no, float(time_sec))


def _load_with_dir(path: Path, direction: str) -> List[Dict[str, Any]]:
    packets = load_data(path)
    for p in packets:
        p["dir"] = direction
    return packets


def load_run_pairs(dir_path: Path) -> List[Tuple[Path, Path]]:
    outs = list_runs(dir_path, pattern="out_*.json")
    ins = list_runs(dir_path, pattern="in_*.json")
    if len(outs) != len(ins):
        raise RuntimeError(f"Run count mismatch in {dir_path}: out={len(outs)} in={len(ins)}")
    return list(zip(outs, ins))


def segment_by_out_boundaries(packets: List[Dict[str, Any]]) -> List[Tuple[str, List[str]]]:
    """
    Start a segment at each OUT; attach subsequent IN packets until next OUT.
    """
    segments: List[Tuple[str, List[str]]] = []
    current_out: str | None = None
    current_in: List[str] = []

    for p in packets:
        payload = p.get("payload")
        if not payload:
            continue

        d = p.get("dir")
        if d == OUT:
            if current_out is not None:
                segments.append((current_out, current_in))
            current_out = payload
            current_in = []
        elif d == IN:
            if current_out is None:
                # IN before first OUT: keep visible (prelude segment)
                current_out = ""
                current_in = [payload]
            else:
                current_in.append(payload)
        else:
            raise RuntimeError(f"Invalid packet dir={d!r}; expected {OUT!r} or {IN!r}")

    if current_out is not None:
        segments.append((current_out, current_in))

    return segments


def run_pairs(dir_path: Path, out_path: Path) -> None:
    parts: List[str] = []

    for run_idx, (out_file, in_file) in enumerate(load_run_pairs(dir_path), start=1):
        out_packets = _load_with_dir(out_file, OUT)
        in_packets = _load_with_dir(in_file, IN)

        merged = [p for p in out_packets if p.get("payload")] + [p for p in in_packets if p.get("payload")]
        merged.sort(key=_sort_key)

        segments = segment_by_out_boundaries(merged)

        parts.append(f"=== RUN {run_idx}: {out_file.name} / {in_file.name} ===\n")
        parts.append(render_segments(segments))
        parts.append("\n")

    write_output("".join(parts), out_path)
    print(f"[ok] written {out_path}")


# ---------- CLI ----------

def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="trace_pairs", description="Export deterministic OUT->IN windows.")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("init")

    p_cal = sub.add_parser("cal")
    p_cal.add_argument("--mode", required=True)

    p_change = sub.add_parser("chng_mode")
    p_change.add_argument("--end", required=True)
    p_change.add_argument("--start", required=True)

    p_meas = sub.add_parser("meas")
    p_meas.add_argument("--mode", required=True)
    p_meas.add_argument("--acq", required=True)

    return p


def main() -> None:
    args = build_parser().parse_args()

    if args.cmd == "init":
        dir_path = LOAD_DIR / "init"
        out_path = OUTPUT_DIR / "trace_pairs" / "init.txt"
        run_pairs(dir_path, out_path)
    
    elif args.cmd == "cal":
        mode = alias(args.mode)
        dir_path = LOAD_DIR / "calibration" / mode
        out_path = OUTPUT_DIR / "trace_pairs" / f"calibration_{mode}.txt"
        run_pairs(dir_path, out_path)

    elif args.cmd == "chng_mode":
        end = alias(args.end)
        start = alias(args.start)
        dir_path = LOAD_DIR / "change_mode" / end / start
        out_path = OUTPUT_DIR / "trace_pairs" / f"change_{end}_from_{start}.txt"
        run_pairs(dir_path, out_path)

    elif args.cmd == "meas":
        mode = alias(args.mode)
        acq = alias(args.acq)
        dir_path = LOAD_DIR / "measurement" / mode / acq
        out_path = OUTPUT_DIR / "trace_pairs" / f"measurement_{mode}_{acq}.txt"
        run_pairs(dir_path, out_path)

    else:
        raise RuntimeError(args.cmd)


if __name__ == "__main__":
    main()
