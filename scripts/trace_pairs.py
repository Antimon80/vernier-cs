"""
trace_pairs.py

Export OUT->IN response windows as a text artifact for protocol RE.
"""

from __future__ import annotations

import argparse
from pathlib import Path
from typing import List, Tuple

from dataset import (
    load_data,
    list_runs,
    write_output,
    OUTPUT_DIR,
    LOAD_DIR,
)
from protocol_tools import hex_payload, segment_responses


def _report_id(payload: str) -> str:
    b = hex_payload(payload)
    return f"{b[0]:02x}" if b else "??"


def _format_groups(byte_tokens: List[str], width: int = 16) -> str:
    lines = []
    for off in range(0, len(byte_tokens), width):
        lines.append(" ".join(byte_tokens[off : off + width]))
    return "\n".join(lines)


def _render_payload(payload: str, indent: str = "") -> str:
    bs = hex_payload(payload)
    toks = [f"{x:02x}" for x in bs]
    block = _format_groups(toks, width=16)
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
                out.append(
                    f"  IN[{k}]: report={_report_id(p)} len={len(hex_payload(p))}"
                )
                out.append(_render_payload(p, indent="  "))
        out.append("\n" + "-" * 72 + "\n")
    return "\n".join(out).rstrip() + "\n"


def load_run_pairs(dir_path: Path) -> List[Tuple[Path, Path]]:
    outs = list_runs(dir_path, pattern="out_*.json")
    ins = list_runs(dir_path, pattern="in_*.json")
    if len(outs) != len(ins):
        raise RuntimeError(
            f"Run count mismatch in {dir_path}: out={len(outs)} in={len(ins)}"
        )
    return list(zip(outs, ins))


def run_pairs(dir_path: Path, out_path: Path, window_ms: float, k: int) -> None:
    texts: List[str] = []

    for run_idx, (out_file, in_file) in enumerate(load_run_pairs(dir_path), start=1):
        out_packets = load_data(out_file)
        in_packets = load_data(in_file)

        merged = [p for p in out_packets if p.get("payload")] + [
            p for p in in_packets if p.get("payload")
        ]

        # Sort by absolute capture time (shared reference), not per-file relative time
        merged.sort(key=lambda p: (p["time_sec"] if p["time_sec"] is not None else 0.0))

        # Recompute a global relative time base for the merged stream
        t0 = next(
            (p["time_sec"] for p in merged if p.get("time_sec") is not None), None
        )
        if t0 is not None:
            for p in merged:
                if p.get("time_sec") is not None:
                    p["time_rel_ms"] = (p["time_sec"] - t0) * 1000.0
                else:
                    p["time_rel_ms"] = None

        segments = segment_responses(merged, window_ms=window_ms, k=k)

        texts.append(f"=== RUN {run_idx}: {out_file.name} / {in_file.name} ===\n")
        texts.append(render_segments(segments))
        texts.append("\n")

    write_output("".join(texts), out_path)
    print(f"[ok] written {out_path}")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="trace_pairs", description="Export OUT->IN response windows."
    )
    sub = p.add_subparsers(dest="cmd", required=True)

    p_init = sub.add_parser("init", help="Export init OUT->IN windows.")
    p_init.add_argument("--window-ms", type=float, default=100.0)
    p_init.add_argument("--k", type=int, default=3)

    p_change = sub.add_parser("change", help="Export change-mode OUT->IN windows.")
    p_change.add_argument("--end", required=True)
    p_change.add_argument("--start", required=True)
    p_change.add_argument("--window-ms", type=float, default=100.0)
    p_change.add_argument("--k", type=int, default=3)

    return p


def main() -> None:
    args = build_parser().parse_args()

    if args.cmd == "init":
        dir_path = LOAD_DIR / "init"
        out_path = OUTPUT_DIR / "trace_pairs" / "init.txt"
        run_pairs(dir_path, out_path, window_ms=args.window_ms, k=args.k)

    elif args.cmd == "change":
        dir_path = LOAD_DIR / "change_mode" / args.end / args.start
        out_path = (
            OUTPUT_DIR / "trace_pairs" / f"change_{args.end}_from_{args.start}.txt"
        )
        run_pairs(dir_path, out_path, window_ms=args.window_ms, k=args.k)

    else:
        raise RuntimeError(args.cmd)


if __name__ == "__main__":
    main()
