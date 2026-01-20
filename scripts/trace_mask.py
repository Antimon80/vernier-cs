"""
trace_mask.py

CLI utility to mask stable bytes across multiple Wireshark HID captures.
"""

from __future__ import annotations

import argparse

from protocol_tools import mask_payloads_across_logs, format_blocks
from dataset import (
    load_init,
    load_change_mode,
    load_change_acquisition,
    load_calibration,
    load_measurement,
    load_payload_runs,
    output_path_init,
    output_path_mode_change,
    output_path_acq_change,
    output_path_calibration,
    output_path_measurement,
    write_output,
)


def run_init() -> None:
    payloads = load_payload_runs(load_init())
    masked = mask_payloads_across_logs(payloads)
    text = format_blocks(masked)
    out_path = output_path_init()
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_mode_change(end: str, start: str) -> None:
    payloads = load_payload_runs(load_change_mode(end=end, start=start))
    masked = mask_payloads_across_logs(payloads)
    text = format_blocks(masked)
    out_path = output_path_mode_change(end, start)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_acq_change(mode: str, end: str, start: str) -> None:
    payloads = load_payload_runs(
        load_change_acquisition(mode=mode, end=end, start=start)
    )
    masked = mask_payloads_across_logs(payloads)
    text = format_blocks(masked)
    out_path = output_path_acq_change(mode, end, start)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_calibration(mode: str) -> None:
    payloads = load_payload_runs(load_calibration(mode=mode))
    masked = mask_payloads_across_logs(payloads)
    text = format_blocks(masked)
    out_path = output_path_calibration(mode)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_measurement(mode: str, acq: str) -> None:
    payloads = load_payload_runs(load_measurement(mode=mode, acq=acq))
    masked = mask_payloads_across_logs(payloads)
    text = format_blocks(masked)
    out_path = output_path_measurement(mode, acq)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="trace_mask",
        description="Mask stable HID payload bytes across multiple Wireshark JSON runs.",
    )
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_init = sub.add_parser("init", help="Mask the initialization sequence.")
    p_init.set_defaults(cmd="init")

    p_change = sub.add_parser("chng_mode", help="Mask a directed mode transition.")
    p_change.add_argument("--end", required=True, help="Target mode (e.g. absorbance).")
    p_change.add_argument("--start", required=True, help="Source mode (e.g. 405nm).")
    p_change.set_defaults(cmd="chng_mode")

    p_acq = sub.add_parser(
        "chng_acq",
        help="Mask a directed acquisition-state transition inside an operating mode.",
    )
    p_acq.add_argument(
        "--mode", required=True, help="Operation mode (e.g. absorbance)."
    )
    p_acq.add_argument(
        "--end", required=True, help="Target acquisition state (e.g. time_resolved)."
    )
    p_acq.add_argument(
        "--start",
        required=True,
        help="Source acquisition state (e.g. full_spectrum, init).",
    )
    p_acq.set_defaults(cmd="chng_acq")

    p_cal = sub.add_parser(
        "cal",
        help="Mask the calibration of the spectrophotometer inside an operating mode.",
    )
    p_cal.add_argument(
        "--mode", required=True, help="Operation mode (absorbance or transmittance)."
    )
    p_cal.set_defaults(cmd="cal")

    p_meas = sub.add_parser(
        "meas", help="Mask a measurement inside an operation and acquisition mode."
    )
    p_meas.add_argument(
        "--mode", required=True, help="Operation mode (e. g. absorbance)."
    )
    p_meas.add_argument(
        "--acq", required=True, help="Acquisition mode (e. g. full_spectrum)."
    )
    p_meas.set_defaults(cmd="meas")

    return parser


def main() -> None:
    args = build_parser().parse_args()
    if args.cmd == "init":
        run_init()
    elif args.cmd == "chng_mode":
        run_mode_change(end=args.end, start=args.start)
    elif args.cmd == "chng_acq":
        run_acq_change(mode=args.mode, end=args.end, start=args.start)
    elif args.cmd == "cal":
        run_calibration(mode=args.mode)
    elif args.cmd == "meas":
        run_measurement(mode=args.mode, acq=args.acq)
    else:
        raise RuntimeError(f"Unknown command: {args.cmd}")


if __name__ == "__main__":
    main()
