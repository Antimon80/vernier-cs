import time
import hid

VID = 0x08F7
PID = 0x0009       # SpectroVisPlus without battery
# PID = 0x0011  # new SpectroVis with battery
# PID = 0x0006  # old SpectroVis
# PID = 0x000a  # UV-Vis Spectrometer
# PID = 0x000D  # Emissions Spectrometer

OUT_FRAMES = [
    bytes.fromhex(
        "00 00 00 fc 00 00 00 07 00 00 00 f5 c3 6d 1d 78 db 19 00 60 b0 7a 71 00 00 00 00 a0 b2 7a 71 00 00 00 00 ec da 19 00 70 e2 19 00 80 46 73 71 00 00 00 00 2c db 19 00 ae 35 5d 71 07 00 00 00 04"
    ),
    bytes.fromhex(
        " 00 00 00 a8 da 19 00 f9 f6 d9 77 00 00 00 00 76 76 43 a2 1f 00 00 00 20 00 00 00 0f 00 00 00 80 da 19 00 c7 4d 41 77 00 db 19 00 70 1b de 77 9e 20 df 77 b0 fa 74 04 00 00 1c 01 00 00 00 00 00"
    ),
    bytes.fromhex(
        " 01 00 00 85 10 5e 71 00 00 00 00 00 00 00 00 41 00 00 00 00 00 00 00 00 00 00 00 e4 07 00 00 41 00 00 00 41 00 00 00 00 00 00 00 a8 da 19 00 f9 f6 d9 77 00 00 00 00 76 76 43 a2 1f 00 00 00 20"
    ),
    bytes.fromhex(
        "02 00 00 f8 46 1c 01 b7 0b df 77 00 00 00 00 00 00 1c 01 28 1e ca 06 c1 01 00 00 08 0e 00 00 00 00 00 00 02 00 00 00 03 00 00 00 00 00 00 00 00 0e 00 00 00 00 03 1d 54 d9 19 00 f9 f6 d9 77 00"
    ),
    bytes.fromhex(
        "ab 00 00 d0 da 19 00 d8 b0 7a 71 c0 da 19 00 60 b0 7a 71 e0 07 00 00 b8 fb 6f 71 00 70 6e 04 2c 00 00 00 f0 4b 74 04 18 83 21 01 48 77 21 01 01 e2 19 00 00 00 00 00 04 00 00 00 fe ff ff ff f4"
    ),
    bytes.fromhex(
        "04 1e 00 00 00 00 00 20 00 00 00 02 00 00 00 00 00 00 00 20 00 00 00 64 00 00 00 d0 df 19 00 f9 f6 d9 77 00 00 00 00 0e 73 43 a2 1f 00 00 00 20 00 00 00 0f 00 00 00 a8 df 19 00 00 00 1c 01 28"
    ),
    bytes.fromhex(
        "04 0f 00 00 00 00 00 d0 fd 74 04 2c df 19 00 c4 5f 5d 71 d0 fd 74 04 cd c7 6d 1d 78 b2 7a 71 f7 08 00 00 4d 60 5d 71 00 00 00 00 39 04 00 00 3c 01 7a 00 48 4a 71 04 7a 00 00 00 00 00 00 00 d0"
    ),
    bytes.fromhex(
        "40 00 00 c8 3b f8 06 00 00 1c 01 74 df 19 00 bd c6 6d 1d 2f 00 00 00 34 df 19 00 64 00 00 00 96 00 57 01 08 f0 6d 04 57 01 00 00 00 00 00 00 08 f0 f7 06 c8 3b f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 c8 3b f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 57 01 08 f0 6d 04 57 01 00 00 00 00 00 00 08 f0 f7 06 c8 3b f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 90 3b f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 56 01 08 f0 6d 04 56 01 00 00 00 00 00 00 08 f0 f7 06 90 3b f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 50 3d f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 5e 01 08 f0 6d 04 5e 01 00 00 00 00 00 00 08 f0 f7 06 50 3d f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 30 37 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 42 01 08 f0 6d 04 42 01 00 00 00 00 00 00 08 f0 f7 06 30 37 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 30 37 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 42 01 08 f0 6d 04 42 01 00 00 00 00 00 00 08 f0 f7 06 30 37 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 d0 39 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 4e 01 08 f0 6d 04 4e 01 00 00 00 00 00 00 08 f0 f7 06 d0 39 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 10 38 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 46 01 08 f0 6d 04 46 01 00 00 00 00 00 00 08 f0 f7 06 10 38 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 98 39 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 4d 01 08 f0 6d 04 4d 01 00 00 00 00 00 00 08 f0 f7 06 98 39 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 98 39 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 4d 01 08 f0 6d 04 4d 01 00 00 00 00 00 00 08 f0 f7 06 98 39 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 08 3a f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 96 00 4f 01 08 f0 6d 04 4f 01 00 00 00 00 00 00 08 f0 f7 06 08 3a f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 e8 3a f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 53 01 08 f0 6d 04 53 01 00 00 00 00 00 00 08 f0 f7 06 e8 3a f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 b0 3a f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 52 01 08 f0 6d 04 52 01 00 00 00 00 00 00 08 f0 f7 06 b0 3a f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 28 39 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 4b 01 08 f0 6d 04 4b 01 00 00 00 00 00 00 08 f0 f7 06 28 39 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 60 39 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 4c 01 08 f0 6d 04 4c 01 00 00 00 00 00 00 08 f0 f7 06 60 39 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 30 37 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 42 01 08 f0 6d 04 42 01 00 00 00 00 00 00 08 f0 f7 06 30 37 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 58 3b f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 55 01 08 f0 6d 04 55 01 00 00 00 00 00 00 08 f0 f7 06 58 3b f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 f0 38 f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 4a 01 08 f0 6d 04 4a 01 00 00 00 00 00 00 08 f0 f7 06 f0 38 f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 88 3d f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 5f 01 08 f0 6d 04 5f 01 00 00 00 00 00 00 08 f0 f7 06 88 3d f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 58 3b f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 55 01 08 f0 6d 04 55 01 00 00 00 00 00 00 08 f0 f7 06 58 3b f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 58 3b f8 06 00 00 1c 01 e4 de 19 00 2d c5 6d 1d 2f 00 00 00 a4 de 19 00 64 00 00 00 94 00 55 01 08 f0 6d 04 55 01 00 00 00 00 00 00 08 f0 f7 06 58 3b f8 06 f8 ef 6d 04 00 00 1c 01 2f"
    ),
    bytes.fromhex(
        "41 00 00 64 00 00 00 ec 00 a9 00 48 4a 71 04 a9 00 00 00 00 00 00 00 d0 eb 74 04 30 07 75 04 38 4a 71 04 00 00 1c 01 13 00 00 00 28 ea 19 00 aa ec d9 77 00 00 00 00 f6 46 43 a2 d0 18 71 04 00"
    ),
    bytes.fromhex(
        "40 00 00 7c 92 dd 77 09 23 df 77 00 00 00 00 08 ea 19 00 00 00 00 00 00 00 00 00 08 ea 19 00 08 ea 19 00 00 00 1c 01 2c ea 19 00 b0 0e 43 77 00 00 00 00 cd 0e 43 77 2d 86 15 90 68 40 ec 06 00"
    ),
]

# expected number of IN packets AFTER each OUT (for logging only)
EXPECTED_IN = [
    0,
    0,
    1,
    56,
    1,
    1,
    1,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    56,
    1,
    56,
]

# Tunables
READ_TIMEOUT_MS = 10  # per hid.read() call (short polling)
STEP_TIMEOUT_MS = 500  # hard cap per OUT->IN burst (milliseconds)
QUIET_WINDOW_MS = (
    30  # stop when no packet arrives for this long after first packet (milliseconds)
)
INTER_OUT_SLEEP = 0.01  # seconds


def normalize_payload(data) -> bytes:
    raw = bytes(data)
    if len(raw) == 65 and raw[0] == 0x00:
        return raw[1:]
    return raw


def write_report(dev, payload64: bytes):
    if len(payload64) != 64:
        raise ValueError(f"OUT frame must be 64 bytes, got {len(payload64)}")
    dev.write(b"\x00" + payload64)  # report id 0


def read_packets(dev):
    """
    Collect one OUT->IN response burst.

    - Hard-stop after STEP_TIMEOUT_MS total.
    - Once the first IN arrives, keep collecting.
    - Stop early if QUIET_WINDOW_MS passes with no further packets.
    - Normalize each payload to exactly 64 bytes (pad/trim) for stable logs.
    """
    got = []
    timeout = time.monotonic() + (STEP_TIMEOUT_MS / 1000.0)

    first_seen = False
    last_packet_time = None  # monotonic seconds

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

        # no data this read()
        if first_seen and last_packet_time is not None:
            quiet_ms = (now - last_packet_time) * 1000.0
            if quiet_ms >= QUIET_WINDOW_MS:
                break

    return got


def main():
    if len(OUT_FRAMES) != len(EXPECTED_IN):
        raise ValueError("OUT_FRAMES and EXPECTED_IN must have same length")

    infos = hid.enumerate(VID, PID)
    if not infos:
        raise RuntimeError(f"No HID device found for VID=0x{VID:04x} PID=0x{PID:04x}")
    info = infos[0]
    dev = hid.Device(path=info["path"])

    out_lines = []
    in_lines = []

    try:
        for i, outp in enumerate(OUT_FRAMES):
            # log OUT
            out_lines.append(f"{i:02d} OUT  {outp.hex(' ')}")

            # send OUT
            write_report(dev, outp)
            time.sleep(INTER_OUT_SLEEP)

            # read IN burst (do NOT stop based on EXPECTED_IN)
            exp = EXPECTED_IN[i]
            ins = read_packets(dev)

            # log IN grouped by step
            in_lines.append(f"--- step {i:02d} expected={exp} got={len(ins)} ---")
            for j, inp in enumerate(ins):
                in_lines.append(f"{i:02d}.{j:04d} IN   {inp.hex(' ')}")

        with open("out.txt", "w", encoding="utf-8") as f:
            f.write("\n".join(out_lines) + "\n")

        with open("in_by_step.txt", "w", encoding="utf-8") as f:
            f.write("\n".join(in_lines) + "\n")

    finally:
        dev.close()


if __name__ == "__main__":
    main()
