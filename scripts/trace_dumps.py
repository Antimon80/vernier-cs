"""
trace_dumps.py

CLI utility to dump raw HID payloads across multiple Wireshark captures.
"""

from __future__ import annotations
import argparse

from protocol_tools import format_raw_blocks
from dataset import (
    load_init,
    load_change_mode,
    load_change_acquisition,
    load_payload_runs,
    output_path_init_dump,
    output_path_mode_change_dump,
    output_path_acq_change_dump,
    write_output,
)


def run_init() -> None:
    payloads = load_payload_runs(load_init())
    text = format_raw_blocks(payloads)
    out_path = output_path_init_dump()
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_mode_change(end: str, start: str) -> None:
    payloads = load_payload_runs(load_change_mode(end=end, start=start))
    text = format_raw_blocks(payloads)
    out_path = output_path_mode_change_dump(end, start)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_acq_change(mode: str, end: str, start: str) -> None:
    payloads = load_payload_runs(
        load_change_acquisition(mode=mode, end=end, start=start)
    )
    text = format_raw_blocks(payloads)
    out_path = output_path_acq_change_dump(mode, end, start)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="trace_dumps",
        description="Dump raw HID payload bytes across multiple Wireshark JSON runs.",
    )
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_init = sub.add_parser("init")
    p_init.set_defaults(cmd="init")

    p_change = sub.add_parser("chng_mode")
    p_change.add_argument("--end", required=True)
    p_change.add_argument("--start", required=True)
    p_change.set_defaults(cmd="chng_mode")

    p_acq = sub.add_parser("chng_acq")
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
        run_mode_change(end=args.end, start=args.start)
    elif args.cmd == "chng_acq":
        run_acq_change(mode=args.mode, end=args.end, start=args.start)
    else:
        raise RuntimeError(args.cmd)


if __name__ == "__main__":
    main()
