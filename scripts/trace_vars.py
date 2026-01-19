"""
trace_vars.py

CLI utility to show the *actual* bytes behind '??' positions in a masked trace.

- Loads multiple Wireshark JSON runs (aligned by packet index).
- Computes the mask across runs.
- For each packet, prints variable offsets and the concrete values per run.

This complements trace_mask.py:
  trace_mask: shows stable bytes, masks varying bytes as '??'
  trace_vars: shows the varying bytes per run at those '??' offsets
"""

from __future__ import annotations

import argparse
from typing import List

from dataset import (
    load_init,
    load_change_mode,
    load_change_acquisition,
    load_payload_runs,
    write_output,
    OUTPUT_DIR,
    alias,
)
from protocol_tools import (
    mask_payloads_across_logs,
    iter_mask_tokens,
    hex_payload,
)


def render_variable_bytes(payloads_by_run: List[List[str]]) -> str:
    """
    payloads_by_run: list of runs, each run is a list of payload strings (colon-hex),
    aligned by packet index.
    """
    if not payloads_by_run:
        return ""

    mask_blocks = mask_payloads_across_logs(payloads_by_run)
    num_packets = min(len(r) for r in payloads_by_run)

    bytes_by_run = [
        [hex_payload(p) for p in run[:num_packets]] for run in payloads_by_run
    ]

    out: List[str] = []
    out.append(f"runs={len(payloads_by_run)} packets(aligned)={num_packets}\n")

    for pkt_idx in range(num_packets):
        tok_mask = iter_mask_tokens(mask_blocks[pkt_idx])
        if not tok_mask:
            continue

        var_offsets = [i for i, t in enumerate(tok_mask) if t == "??"]
        if not var_offsets:
            continue

        out.append(f"packet {pkt_idx}:")
        out.append(" bytes:")

        for off in var_offsets:
            vals = []
            for run_idx, run in enumerate(bytes_by_run):
                b = run[pkt_idx]
                if off < len(b):
                    vals.append(f"run{run_idx+1}={b[off]:02x}")
                else:
                    vals.append(f"run{run_idx+1}=--")
            out.append(f"   @0x{off:02x}: " + " ".join(vals))

        out.append("")

    return "\n".join(out).rstrip() + "\n"


def output_path(name: str) -> str:
    return str(OUTPUT_DIR / "trace_vars" / name)


def run_init() -> None:
    payloads = load_payload_runs(load_init())
    text = render_variable_bytes(payloads)
    out_path = OUTPUT_DIR / "trace_vars" / "init.txt"
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_mode_change(end: str, start: str) -> None:
    payloads = load_payload_runs(load_change_mode(end=end, start=start))
    text = render_variable_bytes(payloads)
    out_path = OUTPUT_DIR / "trace_vars" / f"change_{end}_from_{start}.txt"
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_acq_change(mode: str, end: str, start: str) -> None:
    payloads = load_payload_runs(
        load_change_acquisition(mode=mode, end=end, start=start)
    )
    text = render_variable_bytes(payloads)
    out_path = OUTPUT_DIR / "trace_vars" / f"acq_{mode}_{end}_from_{start}.txt"
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="trace_vars",
        description="Show concrete per-run byte values at '??' mask positions.",
    )
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_init = sub.add_parser("init", help="Analyze init sequence.")
    p_init.set_defaults(cmd="init")

    p_change = sub.add_parser("chng_mode", help="Analyze a directed mode transition.")
    p_change.add_argument("--end", required=True)
    p_change.add_argument("--start", required=True)
    p_change.set_defaults(cmd="chng_mode")

    p_acq = sub.add_parser(
        "chng_acq", help="Analyze a directed acquisition transition."
    )
    p_acq.add_argument("--mode", required=True)
    p_acq.add_argument("--end", required=True)
    p_acq.add_argument("--start", required=True)
    p_acq.set_defaults(cmd="chng_acq")

    return parser


def main() -> None:
    args = build_parser().parse_args()

    if args.cmd == "init":
        run_init()
    elif args.cmd == "chng_mode":
        run_mode_change(end=alias(args.end), start=alias(args.start))
    elif args.cmd == "chng_acq":
        run_acq_change(
            mode=alias(args.mode), end=alias(args.end), start=alias(args.start)
        )
    else:
        raise RuntimeError(f"Unknown command: {args.cmd}")


if __name__ == "__main__":
    main()
