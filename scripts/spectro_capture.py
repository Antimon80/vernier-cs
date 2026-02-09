#!/usr/bin/env python3
import time
from pathlib import Path
import hid
import numpy as np
import matplotlib.pyplot as plt
import argparse
import proto

SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent
OUTPUT_DIR = PROJECT_ROOT / "docs" / "protocol" / "reconstruction" / "spectra"

VID = 0x08F7
PID = [0x0006, 0x0009, 0x0011, 0x000A, 0x000D]

# ------------------ Y SCALING ------------------
# Device sends 16-bit raw counts 0x0000..0xFFFF. Convert to relative intensity 0..1.
COUNTS_MAX = 65535.0

READ_TIMEOUT_MS = 10
STEP_TIMEOUT_MS = 500
QUIET_WINDOW_MS = 30
INTER_OUT_SLEEP = 0.01

# -----------------------------------------------------------------------


def normalize_payload(data) -> bytes:
    raw = bytes(data)
    if len(raw) == 65 and raw[0] == 0x00:
        return raw[1:]
    return raw


def write_report(dev, payload: bytes):
    """
    Write a HID report with report-id 0.
    Payload is padded with 0x00 up to 64 bytes if shorter.
    """
    if len(payload) > 64:
        raise ValueError(f"OUT frame must be <= 64 bytes, got {len(payload)}")

    if len(payload) < 64:
        payload = payload + b"\x00" * (64 - len(payload))

    dev.write(b"\x00" + payload)


def read_packets(dev):
    got = []
    timeout = time.monotonic() + (STEP_TIMEOUT_MS / 1000.0)
    first_seen = False
    last_packet_time = None

    while time.monotonic() < timeout:
        data = dev.read(65, READ_TIMEOUT_MS)
        now = time.monotonic()

        if data:
            payload = normalize_payload(data)
            if len(payload) != 64:
                payload = (payload + b"\x00" * 64)[:64]
            got.append(payload)
            first_seen = True
            last_packet_time = now
            continue

        if first_seen and last_packet_time is not None:
            quiet_ms = (now - last_packet_time) * 1000.0
            if quiet_ms >= QUIET_WINDOW_MS:
                break

    return got


def concat_payloads(payloads64):
    if not payloads64:
        return b""
    return b"".join(payloads64)


def bytes_to_u16_le(buf: bytes) -> np.ndarray:
    n = (len(buf) // 2) * 2
    return np.frombuffer(buf[:n], dtype="<u2").copy()


def build_linear_axis(
    n_points: int, samples_min: float, samples_max: float
) -> np.ndarray:
    if n_points < 2:
        return np.array([samples_min], dtype=np.float64)
    return np.linspace(samples_min, samples_max, n_points, dtype=np.float64)


def save_spectrum(path: str, samples: np.ndarray, counts_u16: np.ndarray):
    """
    Save spectrum as TSV:
    nm <tab> counts <tab> rel_intensity
    """
    samples = np.asarray(samples, dtype=np.float64)
    counts = np.asarray(counts_u16, dtype=np.uint16)

    n = min(samples.size, counts.size)
    rel = counts[:n].astype(np.float64) / COUNTS_MAX

    with open(path, "w", encoding="utf-8") as f:
        f.write("# wavelength_nm\tcounts_u16\trel_intensity\n")
        for i in range(n):
            f.write(f"{samples[i]:.6f}\t{int(counts[i])}\t{rel[i]:.10f}\n")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="cmd", required=True)

    p_init = sub.add_parser("init")
    p_init.set_defaults(cmd="init")
    p_init.add_argument("--type", required=True)

    p_change_mode = sub.add_parser("chng_mode")
    p_change_mode.set_defaults(cmd="chng_mode")
    p_change_mode.add_argument("--type", required=True)

    p_meas = sub.add_parser("meas")
    p_meas.set_defaults(cmd="meas")

    p_close = sub.add_parser("close")
    p_close.set_defaults(cmd="close")
    p_close.add_argument("--type", required=True)

    return parser


def main():
    infos = []
    for pid in PID:
        infos.extend(hid.enumerate(VID, pid))

    if not infos:
        raise RuntimeError(f"No HID device found for VID=0x{VID:04x} PID=0x{PID:04x}")
    dev = hid.Device(path=infos[0]["path"])

    dev_pid = infos[0].get("product_id")

    PID_TO_NAME = {
        0x0006: "spectrovis",
        0x0009: "spectrovis_plus",
        0x0011: "spectrovis_plus_ble",
        0x000A: "uv_vis",
        0x000D: "emission",
    }

    out_frames = []
    args = build_parser().parse_args()
    if args.cmd == "init":
        if args.type == "sv":
            out_frames = proto.spectrovis_init
        elif args.type == "sv_plus":
            out_frames = proto.spectrovis_plus_init
        elif args.type == "sv_plus_ble":
            out_frames = proto.spectrovis_plus_ble_init
        elif args.type == "uv_vis":
            out_frames = proto.uv_vis_init
        elif args.type == "emission":
            out_frames = proto.emission_init

        for out in out_frames:
            write_report(dev, out)
            time.sleep(INTER_OUT_SLEEP)
            _ = read_packets(dev)

        print("Device initialized.")

    elif args.cmd == "chng_mode":
        if args.type == "sv" or args.type == "uv_vis":
            out_frames = proto.absorbance_to_intensity_spectrovis
        elif args.type == "sv_plus" or args.type == "sv_plus_ble":
            out_frames = proto.absorbance_to_intensity_spectrovis_plus

        for out in out_frames:
            write_report(dev, out)
            time.sleep(INTER_OUT_SLEEP)
            _ = read_packets(dev)

        print("Changed mode to 'intensity'.")

    elif args.cmd == "meas":
        write_report(dev, proto.measurement[0])
        time.sleep(INTER_OUT_SLEEP)
        print("Measurement request sent.")

        meas_in = read_packets(dev)
        meas_buf = concat_payloads(meas_in)
        meas_u16 = bytes_to_u16_le(meas_buf)

        if meas_u16.size == 0:
            raise RuntimeError("No measurement data decoded (u16 length 0)")

        # X-axis: FULL RAW RANGE, linearly mapped over ALL samples
        sample_no = build_linear_axis(meas_u16.size, 1, len(meas_u16))

        # Y-axis: counts -> relative intensity 0..1 (NO max-normalization)
        y_rel = meas_u16.astype(np.float64) / COUNTS_MAX

        # Save (use the same x-window as for plot)
        spectrometer = PID_TO_NAME.get(
            dev_pid, f"unknown_{dev_pid:04x}" if dev_pid is not None else "unknown"
        )

        save_spectrum(OUTPUT_DIR / f"{spectrometer}_argon.tsv", sample_no, meas_u16)

        title = f"Spectrum (RAW {1}-{len(meas_u16)} samples)"
        plt.figure()
        plt.plot(sample_no, y_rel)
        plt.xlabel("Sample index (raw CCD pixel)")
        plt.ylabel("Relative intensity (counts / 65535)")
        plt.title(title)
        plt.grid(True)
        plt.show()

    elif args.cmd == "close":
        if args.type == "sv":
            out_frames = proto.spectrovis_close
        elif args.type == "sv_plus" or args.type == "sv_plus_ble":
            out_frames = proto.spectrovis_plus_close
        elif args.type == "uv_vis" or args.type == "emission":
            out_frames = proto.spectrovis_close

        for out in out_frames:
            write_report(dev, out)
            time.sleep(INTER_OUT_SLEEP)
            _ = read_packets(dev)

        dev.close()

        print("Device closed.")


if __name__ == "__main__":
    main()
