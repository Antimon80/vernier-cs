import time
import hid

VID = 0x08F7
PID = 0x0009

OUT_FRAMES = [
    bytes.fromhex(
        "41 00 00 64 00 00 00 96 00 68 00 e8 1a 63 04 68 00 00 00 00 00 00 00 10 ee 07 03 90 fe 07 03 d8 1a 63 04 00 00 0f 01 13 00 00 00 e8 e8 19 00 aa ec 18 77 00 00 00 00 be 49 04 ca e0 0d 64 04 01"
    ),
    bytes.fromhex(
        "42 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff ff ff ff 00 00 00 00 6c e8 19 00 11 4c 10 cb 54 e9 19 00 e0 9b e8 74 24 67 1c 40 fe ff ff ff d4 e8 19 00 4f"
    ),
    bytes.fromhex(
        "43 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff ff ff ff 00 00 00 00 6c e8 19 00 11 4c 10 cb 54 e9 19 00 e0 9b e8 74 24 67 1c 40 fe ff ff ff d4 e8 19 00 4f"
    ),
    bytes.fromhex(
        "41 01 00 cd 0e e8 74 90 b2 f2 34 34 08 00 00 00 00 00 00 20 d7 18 76 24 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 c0 f2 fc ff ff"
    ),
    bytes.fromhex(
        "40 00 00 7c 92 1c 77 09 23 1e 77 00 00 00 00 c8 e8 19 00 00 00 00 00 00 00 00 00 c8 e8 19 00 c8 e8 19 00 00 00 0f 01 ec e8 19 00 b0 0e e8 74 00 00 00 00 cd 0e e8 74 b8 b2 f2 34 a0 1d 07 03 00"
    ),
    bytes.fromhex(
        "04 0f 00 00 00 00 00 20 00 00 00 02 00 00 00 00 00 00 00 20 00 00 00 00 00 00 00 80 e8 19 00 f9 f6 18 77 00 00 00 00 d6 49 04 ca 1f 00 00 00 20 00 00 00 0f 00 00 00 58 e8 19 00 00 00 0f 01 d8"
    ),
]

# expected number of IN packets AFTER each OUT
EXPECTED_IN = [1, 1, 1, 1, 56, 1]

READ_TIMEOUT_MS = 200  # per read() call
STEP_TIMEOUT_MS = 20000  # max time per step to collect expected IN packets
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


def read_exact(dev, n: int):
    got = []
    deadline = time.monotonic() + (STEP_TIMEOUT_MS / 1000.0)

    while len(got) < n and time.monotonic() < deadline:
        data = dev.read(65, READ_TIMEOUT_MS)
        if not data:
            continue
        payload = normalize_payload(data)
        # keep log stable even if a short report occurs
        if len(payload) != 64:
            payload = (payload + b"\x00" * 64)[:64]
        got.append(payload)

    return got


def main():
    if len(OUT_FRAMES) != len(EXPECTED_IN):
        raise ValueError("OUT_FRAMES and EXPECTED_IN must have same length")

    info = hid.enumerate(VID, PID)[0]
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

            # read expected IN
            exp = EXPECTED_IN[i]
            ins = read_exact(dev, exp)

            # log IN grouped by step
            in_lines.append(f"--- step {i:02d} expected={exp} got={len(ins)} ---")
            for j, inp in enumerate(ins):
                in_lines.append(f"{i:02d}.{j:04d} IN   {inp.hex(' ')}")

        # write files
        with open("out.txt", "w", encoding="utf-8") as f:
            f.write("\n".join(out_lines) + "\n")

        with open("in_by_step.txt", "w", encoding="utf-8") as f:
            f.write("\n".join(in_lines) + "\n")

    finally:
        dev.close()


if __name__ == "__main__":
    main()
