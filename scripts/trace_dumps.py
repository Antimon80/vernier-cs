"""
trace_dumps.py

CLI utility to dump raw HID payloads across multiple Wireshark captures.
"""

from __future__ import annotations
import argparse

from protocol_tools import format_raw_blocks
from dataset import *


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


def run_calibration(mode: str) -> None:
    payloads = load_payload_runs(load_calibration(mode=mode))
    text = format_raw_blocks(payloads)
    out_path = output_path_calibration_dump(mode)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_experiment(mode: str, acq: str) -> None:
    payloads = load_payload_runs(load_experiment(mode=mode, acq=acq))
    text = format_raw_blocks(payloads)
    out_path = output_path_experiment_dump(mode, acq)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_measurement(mode: str, acq: str) -> None:
    payloads = load_payload_runs(load_measurement(mode=mode, acq=acq))
    text = format_raw_blocks(payloads)
    out_path = output_path_measurement_dump(mode, acq)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_acq_params(mode: str, acq: str) -> None:
    payloads = load_payload_runs(load_acquisition_params(mode=mode, acq=acq))
    text = format_raw_blocks(payloads)
    out_path = output_path_acq_params_dump(mode, acq)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_recalibration(mode: str, acq: str) -> None:
    payloads = load_payload_runs(load_recalibration(mode=mode, acq=acq))
    text = format_raw_blocks(payloads)
    out_path = output_path_recal_dump(mode, acq)
    write_output(text, out_path)
    print(f"[ok] written {out_path}")


def run_close() -> None:
    payloads = load_payload_runs(load_close())
    text = format_raw_blocks(payloads)
    out_path = output_path_close_dump()
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

    p_cal = sub.add_parser("cal")
    p_cal.add_argument("--mode", required=True)
    p_cal.set_defaults(cmd="cal")

    p_exp = sub.add_parser("exp")
    p_exp.add_argument("--mode", required=True)
    p_exp.add_argument("--acq", required=True)
    p_exp.set_defaults(cmd="exp")

    p_meas = sub.add_parser("meas")
    p_meas.add_argument("--mode", required=True)
    p_meas.add_argument("--acq", required=True)
    p_meas.set_defaults(cmd="meas")

    p_acq_param = sub.add_parser("acq_param")
    p_acq_param.add_argument("--mode", required=True)
    p_acq_param.add_argument("--acq", required=True)
    p_acq_param.set_defaults(cmd="acq_param")

    p_recal = sub.add_parser("recal")
    p_recal.add_argument("--mode", required=True)
    p_recal.add_argument("--acq", required=True)
    p_recal.set_defaults(cmd="recal")

    p_close = sub.add_parser("close")
    p_close.set_defaults(cmd="close")

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
    elif args.cmd == "exp":
        run_experiment(mode=args.mode, acq=args.acq)
    elif args.cmd == "meas":
        run_measurement(mode=args.mode, acq=args.acq)
    elif args.cmd == "acq_param":
        run_acq_params(mode=args.mode, acq=args.acq)
    elif args.cmd == "recal":
        run_recalibration(mode=args.mode, acq=args.acq)
    elif args.cmd == "close":
        run_close()
    else:
        raise RuntimeError(args.cmd)


if __name__ == "__main__":
    main()
