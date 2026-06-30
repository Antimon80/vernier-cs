import time
import hid

VID = 0x08F7
PID = 0x0009  # SpectroVisPlus without battery
# PID = 0x0011  # new SpectroVis with battery
# PID = 0x0006  # old SpectroVis
# PID = 0x000a  # UV-Vis Spectrometer
# PID = 0x000D  # Emissions Spectrometer

OUT_FRAMES = [
    bytes.fromhex(
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
    bytes.fromhex(
        "01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
    bytes.fromhex(
        "02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
    bytes.fromhex(
        "04 1e 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    ),
    bytes.fromhex(
        "04 32 00 00 00 00 00 a0 81 71 04 2c df 19 00 c4 5f 82 70 a0 81 71 04 c3 ae 3a 2d 78 b2 9f 70 f7 08 00 00 4d 60 82 70 00 00 00 00 bf 02 00 00 01 06 66 00 b0 4f 61 04 66 00 00 00 00 00 00 00 90"
    ),
    bytes.fromhex(
        "40 00 00 30 88 3b 03 00 00 0f 01 74 df 19 00 b3 af 3a 2d 2f 00 00 00 34 df 19 00 64 00 00 00 df 00 6b 00 28 58 61 04 6b 00 00 00 00 00 00 00 08 70 3b 03 30 88 3b 03 18 58 61 04 00 00 0f 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 d0 8a 3b 03 00 00 0f 01 e4 de 19 00 23 ac 3a 2d 2f 00 00 00 a4 de 19 00 64 00 00 00 df 00 77 00 28 58 61 04 77 00 00 00 00 00 00 00 08 70 3b 03 d0 8a 3b 03 18 58 61 04 00 00 0f 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 f0 89 3b 03 00 00 0f 01 e4 de 19 00 23 ac 3a 2d 2f 00 00 00 a4 de 19 00 64 00 00 00 df 00 73 00 28 58 61 04 73 00 00 00 00 00 00 00 08 70 3b 03 f0 89 3b 03 18 58 61 04 00 00 0f 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 70 86 3b 03 00 00 0f 01 e4 de 19 00 23 ac 3a 2d 2f 00 00 00 a4 de 19 00 64 00 00 00 df 00 63 00 28 58 61 04 63 00 00 00 00 00 00 00 08 70 3b 03 70 86 3b 03 18 58 61 04 00 00 0f 01 2f"
    ),
    bytes.fromhex(
        "40 00 00 d8 88 3b 03 00 00 0f 01 e4 de 19 00 23 ac 3a 2d 2f 00 00 00 a4 de 19 00 64 00 00 00 df 00 6e 00 28 58 61 04 6e 00 00 00 00 00 00 00 08 70 3b 03 d8 88 3b 03 18 58 61 04 00 00 0f 01 2f"
    ),
    bytes.fromhex(
        "41 00 00 64 00 00 00 ab 05 b9 00 b0 4f 61 04 b9 00 00 00 00 00 00 00 90 70 71 04 70 8e 71 04 a0 4f 61 04 00 00 0f 01 13 00 00 00 28 ea 19 00 aa ec 18 77 00 00 00 00 7e 4b 04 ca e0 15 59 04 01"
    ),
    bytes.fromhex(
        "42 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff ff ff ff 00 00 00 00 ac e9 19 00 0d 80 0c 7a 94 ea 19 00 e0 9b e8 74 24 67 1c 40 fe ff ff ff 14 ea 19 00 4f"
    ),
    bytes.fromhex(
        "43 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff ff ff ff 00 00 00 00 ac e9 19 00 0d 80 0c 7a 94 ea 19 00 e0 9b e8 74 24 67 1c 40 fe ff ff ff 14 ea 19 00 4f"
    ),
    bytes.fromhex(
        "41 01 00 cd 0e e8 74 50 b0 f2 34 34 08 00 00 00 00 00 00 20 d7 18 76 24 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff"
    ),
    bytes.fromhex(
        "40 00 00 7c 92 1c 77 09 23 1e 77 00 00 00 00 08 ea 19 00 00 00 00 00 00 00 00 00 08 ea 19 00 08 ea 19 00 00 00 0f 01 2c ea 19 00 b0 0e e8 74 00 00 00 00 cd 0e e8 74 78 b0 f2 34 d0 3d 1a 03 00"
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
