from __future__ import annotations
from typing import List, Tuple

from protocol_tools import hex_payload
from dataset import *

MIN_RUN = 64
LINEAR_TOL = 3


def decode_u16_le(buf: bytes) -> List[int]:
    return [buf[i] | (buf[i + 1] << 8) for i in range(0, len(buf) - 1, 2)]

def decode_u16_be(buf: bytes) -> List[int]:
    out: List[int] = []
    for i in range(0, len(buf) - 1, 2):
        out.append((buf[i] << 8) | buf[i + 1])
    return out


def build_buf(block: List[str]) -> bytes:
    buf = bytearray()
    for p in block:
        buf.extend(hex_payload(p))
    return bytes(buf)


def median(xs: List[int]) -> int:
    ys = sorted(xs)
    n = len(ys)
    mid = n // 2
    return ys[mid] if (n % 2 == 1) else (ys[mid - 1] + ys[mid]) // 2


def mad(xs: List[int], m: int) -> int:
    return median([abs(x - m) for x in xs]) if xs else 0


def increasing_runs(vals: List[int]) -> List[Tuple[int, int]]:
    n = len(vals)
    if n == 0:
        return []
    runs: List[Tuple[int, int]] = []
    s = 0
    for i in range(1, n):
        if vals[i] <= vals[i - 1]:
            runs.append((s, i - s))
            s = i
    runs.append((s, n - s))
    return runs


def summarize(vals: List[int], s: int, ln: int) -> Tuple[List[int], List[int]]:
    head = vals[s : min(s + 10, s + ln)]
    tail_start = max(s, s + ln - 10)
    tail = vals[tail_start : s + ln]
    return head, tail


def trim_to_linear_core(vals: List[int], s: int, ln: int, tol: int) -> Tuple[int, int, int, int, int]:
    """
    Given a strictly increasing run [s, s+ln), find the longest contiguous sub-run
    where deltas are within [d_med - tol, d_med + tol].

    Returns (s2, ln2, d_med, d_mad, bad_tol) for the trimmed core.
    """
    if ln < 3:
        return s, ln, 0, 0, 0

    ds = [vals[s + i + 1] - vals[s + i] for i in range(ln - 1)]
    d_med = median(ds)
    d_mad = mad(ds, d_med)
    lo, hi = d_med - tol, d_med + tol

    good = [(lo <= d <= hi) for d in ds]

    best_i = 0
    best_k = 0
    i = 0
    while i < len(good):
        if not good[i]:
            i += 1
            continue
        j = i
        while j < len(good) and good[j]:
            j += 1
        k = j - i
        if k > best_k:
            best_i = i
            best_k = k
        i = j

    if best_k == 0:
        bad = sum(1 for g in good if not g)
        return s, ln, d_med, d_mad, bad

    s2 = s + best_i
    ln2 = best_k + 1

    ds2 = [vals[s2 + i + 1] - vals[s2 + i] for i in range(ln2 - 1)]
    d_med2 = median(ds2)
    d_mad2 = mad(ds2, d_med2)
    lo2, hi2 = d_med2 - tol, d_med2 + tol
    bad2 = sum(1 for d in ds2 if d < lo2 or d > hi2)

    return s2, ln2, d_med2, d_mad2, bad2


def pick_best_increasing_run(vals: List[int]) -> Tuple[int, int]:
    runs = increasing_runs(vals)
    cand = [(s, ln) for (s, ln) in runs if ln >= MIN_RUN]
    if cand:
        return max(cand, key=lambda x: x[1])
    return max(runs, key=lambda x: x[1]) if runs else (-1, 0)


def run() -> None:
    runs = load_payload_runs(load_init(direction=IN))

    for run_idx, payloads in enumerate(runs, start=1):
        print(f"\n=== RUN {run_idx} ===")

        after_01 = payloads[1:]
        first = hex_payload(after_01[0])
        payload_len = len(first)
        print(f"payload_len = {payload_len} bytes")

        if payload_len == 8:
            packet_count = 128
            block = after_01[:packet_count]
            buf = build_buf(block)

            vals_all = decode_u16_be(buf)
            vals = vals_all[1::2]
        elif payload_len == 64:
            packet_count = 56
            block = after_01[:packet_count]
            buf = build_buf(block)
            vals = decode_u16_le(buf)
        else:
            print(f"unexpected packet size {payload_len}")
            continue

        s, ln = pick_best_increasing_run(vals)
        s2, ln2, d_med, d_mad, bad = trim_to_linear_core(vals, s, ln, LINEAR_TOL)

        head, tail = summarize(vals, s2, ln2)

        print(f"u16_total={len(vals)} linear_core start={s2} len={ln2}")
        print(f"  d_med={d_med} d_mad={d_mad} bad_tol={bad} tol=±{LINEAR_TOL}")
        print(f"  head={head}")
        print(f"  tail={tail}")


if __name__ == "__main__":
    run()