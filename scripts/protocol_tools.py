"""
protocol_tools.py

Generic helpers for protocol reverse engineering:
- hex parsing
- masking stable bytes across multiple runs
- formatting and output
"""

from __future__ import annotations

from typing import List

def hex_payload(colon_hex: str) -> bytes:
    """Convert a colon-separated hex byte string into raw bytes."""
    return bytes(int(b, 16) for b in colon_hex.split(":"))


def format_groups(byte_tokens: List[str], width: int = 32) -> str:
    """Format a flat list of byte tokens into fixed-width rows."""
    lines: List[str] = []
    for off in range(0, len(byte_tokens), width):
        lines.append(" ".join(byte_tokens[off : off + width]))
    return "\n".join(lines)


def format_blocks(blocks: List[str]) -> str:
    """Join packet blocks separated by a blank line (nice for side-by-side viewing)."""
    return "\n\n".join(blocks) + "\n"


def mask_payloads_across_logs(payloads_by_log: List[List[str]]) -> List[str]:
    """
    Compute a per-packet mask across multiple logs (aligned by packet index).

    Returns one formatted block per packet index.

    Formatting matches `format_raw_blocks()` style for one packet:
      packet N:
        xx xx xx ... (32 bytes per line, indented by two spaces)

    Identical bytes across all runs remain visible; differing bytes become '??'.
    """
    if not payloads_by_log:
        return []

    num_packets = min(len(log) for log in payloads_by_log)
    masked_blocks: List[str] = []

    for pkt_idx in range(num_packets):
        bs = [hex_payload(log[pkt_idx]) for log in payloads_by_log]
        min_len = min(len(b) for b in bs)

        tokens: List[str] = []
        for j in range(min_len):
            b0 = bs[0][j]
            tokens.append(f"{b0:02x}" if all(b[j] == b0 for b in bs) else "??")

        lines: List[str] = []
        lines.append(f"packet {pkt_idx}:")
        for off in range(0, len(tokens), 32):
            lines.append("  " + " ".join(tokens[off : off + 32]))

        masked_blocks.append("\n".join(lines))

    return masked_blocks


def format_raw_blocks(payloads_by_run: list[list[str]]) -> str:
    """
    Format raw payloads grouped by run.

    Output:
      === RUN 1 ===
      packet 0:
        xx xx xx ...
      packet 1:
        xx xx xx ...

      === RUN 2 ===
      ...
    """
    lines: list[str] = []

    for run_idx, run in enumerate(payloads_by_run, start=1):
        lines.append(f"=== RUN {run_idx} ===")
        for pkt_idx, payload in enumerate(run):
            bytes_ = payload.split(":")
            lines.append(f"packet {pkt_idx}:")
            for off in range(0, len(bytes_), 32):
                lines.append("  " + " ".join(bytes_[off:off+32]))
        lines.append("")

    return "\n".join(lines).rstrip() + "\n"

def iter_mask_tokens(mask_block: str) -> List[str]:
    """
    Convert a formatted mask block back into a flat token list.
    Tokens are like '41', '0f', '??'.
    """
    tokens: List[str] = []
    for line in mask_block.splitlines():
        line = line.strip()
        if not line:
            continue
        tokens.extend(line.split())
    return tokens