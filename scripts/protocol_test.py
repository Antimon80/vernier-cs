import time
import hid

VID = 0x08F7
# PID = 0x0009       # SpectroVisPlus without battery
PID = 0x0011  # new SpectroVis with battery
# PID = 0x0006  # old SpectroVis

OUT_FRAMES = [
    bytes.fromhex(
        "41 00 00 64 00 00 00 07 00 11 00 50 98 21 03 11 00 00 00 00 00 00 00 a0 1d 07 03 70 20 07 03 40 98 21 03 00 00 0f 01 13 00 00 00 a0 ea 19 00 aa ec 18 77 00 00 00 00 f6 4b 04 ca 00 00 00 00 00"
    ),
    bytes.fromhex(
        "42 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff ff ff ff 00 00 00 00 24 ea 19 00 14 54 15 1d 0c eb 19 00 e0 9b e8 74 24 67 1c 40 fe ff ff ff 8c ea 19 00 4f"
    ),
    bytes.fromhex(
        "43 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff ff ff ff 00 00 00 00 24 ea 19 00 14 54 15 1d 0c eb 19 00 e0 9b e8 74 24 67 1c 40 fe ff ff ff 8c ea 19 00 4f"
    ),
    bytes.fromhex(
        "40 00 00 7c 92 1c 77 09 23 1e 77 00 00 00 00 80 ea 19 00 00 00 00 00 00 00 00 00 80 ea 19 00 80 ea 19 00 00 00 0f 01 a4 ea 19 00 b0 0e e8 74 00 00 00 00 cd 0e e8 74 f0 b0 f2 34 c8 0c 21 03 00"
    ),
]

# expected number of IN packets AFTER each OUT (for logging only)
EXPECTED_IN = [1, 1, 1, 56]

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


def read_burst(dev):
    """
    Collect one OUT->IN response burst.

    - Hard-stop after STEP_TIMEOUT_MS total.
    - Once the first IN arrives, keep collecting.
    - Stop early if QUIET_WINDOW_MS passes with no further packets.
    - Normalize each payload to exactly 64 bytes (pad/trim) for stable logs.
    """
    got = []
    hard_deadline = time.monotonic() + (STEP_TIMEOUT_MS / 1000.0)

    first_seen = False
    last_packet_time = None  # monotonic seconds

    while time.monotonic() < hard_deadline:
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
            ins = read_burst(dev)

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
