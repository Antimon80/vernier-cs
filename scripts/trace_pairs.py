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
from typing import List, Tuple, Dict, Any

from dataset import (
    load_data,
    list_runs,
    write_output,
    OUTPUT_DIR,
    LOAD_DIR,
    IN,
    OUT,
)
from protocol_tools import hex_payload, format_groups


def _report_id(payload: str) -> str:
    b = hex_payload(payload)
    return f"{b[0]:02x}" if b else "??"


def _render_payload(payload: str, indent: str = "") -> str:
    bs = hex_payload(payload)
    toks = [f"{x:02x}" for x in bs]
    block = format_groups(toks, width=32)
    return "\n".join(indent + line for line in block.splitlines())


def render_segments(segments: List[Tuple[str, List[str]]]) -> str:
    out: List[str] = []
    for i, (out_payload, resp) in enumerate(segments):
        out.append(
            f"OUT #{i}: report={_report_id(out_payload)} len={len(hex_payload(out_payload))}"
        )
        out.append(_render_payload(out_payload))
        if not resp:
            out.append("  IN: (none)")
        else:
            for k, p in enumerate(resp):
                out.append(f"  IN[{k}]: report={_report_id(p)} len={len(hex_payload(p))}")
                out.append(_render_payload(p, indent="  "))
        out.append("\n" + "-" * 72 + "\n")
    return "\n".join(out).rstrip() + "\n"


def load_run_pairs(dir_path: Path) -> List[Tuple[Path, Path]]:
    outs = list_runs(dir_path, pattern="out_*.json")
    ins = list_runs(dir_path, pattern="in_*.json")
    if len(outs) != len(ins):
        raise RuntimeError(f"Run count mismatch in {dir_path}: out={len(outs)} in={len(ins)}")
    return list(zip(outs, ins))


def _load_with_dir(path: Path, direction: str) -> List[Dict[str, Any]]:
    """
    Load packets from one file and annotate each packet with direction ("out"|"in").
    """
    packets = load_data(path)
    for p in packets:
        p["dir"] = direction
    return packets


def _sort_key(p: Dict[str, Any]) -> Tuple[int, float]:
    """
    Deterministic global ordering.
    Prefer Wireshark's frame number (global capture order).
    Fall back to time_sec if frame_no missing.
    """
    frame_no = int(p.get("frame_no", -1))
    time_sec = p.get("time_sec")
    if time_sec is None:
        time_sec = float("inf")
    return (frame_no, float(time_sec))


def segment_by_out_boundaries(packets: List[Dict[str, Any]]) -> List[Tuple[str, List[str]]]:
    """
    Deterministic segmentation:
    - Start a new segment at each OUT packet.
    - Attach all subsequent IN packets until the next OUT.
    """
    segments: List[Tuple[str, List[str]]] = []
    current_out: str | None = None
    current_in: List[str] = []

    for p in packets:
        payload = p.get("payload")
        if not payload:
            continue

        direction = p.get("dir")
        if direction == OUT:
            if current_out is not None:
                segments.append((current_out, current_in))
            current_out = payload
            current_in = []
        elif direction == IN:
            if current_out is None:
                # IN before first OUT: keep it visible instead of silently dropping.
                # (Alternative: drop, or collect in a separate "prelude" section.)
                current_out = ""
                current_in = [payload]
            else:
                current_in.append(payload)
        else:
            raise RuntimeError(f"Invalid packet dir={direction!r}; expected {OUT!r} or {IN!r}")

    if current_out is not None:
        segments.append((current_out, current_in))

    return segments


def run_pairs(dir_path: Path, out_path: Path) -> None:
    texts: List[str] = []

    for run_idx, (out_file, in_file) in enumerate(load_run_pairs(dir_path), start=1):
        out_packets = _load_with_dir(out_file, OUT)
        in_packets = _load_with_dir(in_file, IN)

        merged = [p for p in out_packets if p.get("payload")] + [p for p in in_packets if p.get("payload")]

        # Deterministic ordering: global capture order via frame_no (then time_sec fallback).
        merged.sort(key=_sort_key)

        # Optional: recompute a global relative base for the merged stream (kept for debugging).
        t0 = next((p.get("time_sec") for p in merged if p.get("time_sec") is not None), None)
        if t0 is not None:
            for p in merged:
                ts = p.get("time_sec")
                p["time_rel_ms"] = (ts - t0) * 1000.0 if ts is not None else None
        else:
            for p in merged:
                p["time_rel_ms"] = None

        segments = segment_by_out_boundaries(merged)

        texts.append(f"=== RUN {run_idx}: {out_file.name} / {in_file.name} ===\n")
        texts.append(render_segments(segments))
        texts.append("\n")

    write_output("".join(texts), out_path)
    print(f"[ok] written {out_path}")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="trace_pairs", description="Export deterministic OUT->IN windows.")
    sub = p.add_subparsers(dest="cmd", required=True)

    sub.add_parser("init")

    p_change = sub.add_parser("change")
    p_change.add_argument("--end", required=True)
    p_change.add_argument("--start", required=True)

    return p


def main() -> None:
    args = build_parser().parse_args()

    if args.cmd == "init":
        dir_path = LOAD_DIR / "init"
        out_path = OUTPUT_DIR / "trace_pairs" / "init.txt"
        run_pairs(dir_path, out_path)

    elif args.cmd == "change":
        dir_path = LOAD_DIR / "change_mode" / args.end / args.start
        out_path = OUTPUT_DIR / "trace_pairs" / f"change_{args.end}_from_{args.start}.txt"
        run_pairs(dir_path, out_path)

    else:
        raise RuntimeError(args.cmd)


if __name__ == "__main__":
    main()
