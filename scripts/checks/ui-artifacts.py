#!/usr/bin/env python3
"""Require the reviewed UI scenario inventory and validate PNG sidecars."""
from __future__ import annotations

import hashlib
import struct
import sys
from pathlib import Path

EXPECTED = {
    "provider/storage-unknown-1280x820.png": (1280, 820),
    "provider/storage-none-1280x820.png": (1280, 820),
    "provider/storage-positive-1280x820.png": (1280, 820),
    "provider/metrics-help-1280x820.png": (1280, 820),
    "provider/metrics-large-1120x820.png": (1120, 820),
    **{
        f"provider/metrics-{width}x{height}.png": (width, height)
        for width, height in [(720, 560), (900, 700), (1280, 820), (1600, 900)]
    },
    **{
        f"provider/metrics-{outcome}-1280x820.png": (1280, 820)
        for outcome in ["completed", "cancelled", "failed"]
    },
    **{
        f"provider/ready-{width}x{height}.png": (width, height)
        for width, height in [(720, 560), (900, 700), (1280, 820), (1600, 900)]
    },
    **{
        f"provider/{scenario}-1280x820.png": (1280, 820)
        for scenario in ["after-install", "after-start", "after-stop", "update", "maintenance",
                         "failure-detect", "failure-update", "failure-storage"]
    },
    **{
        f"selector/panel-ready-{width}x{height}.png": (width, height)
        for width, height in [(720, 560), (900, 700), (1280, 820), (1600, 900)]
    },
    "selector/compact-1600x900.png": (1600, 900),
    "selector/panel-keyboard-720x560.png": (720, 560),
    "selector/confirmed-mismatch-900x700.png": (900, 700),
    "selector/custom-working-draft-900x700.png": (900, 700),
    "selector/compatibility-900x700.png": (900, 700),
    "selector/empty-720x560.png": (720, 560),
    "selector/long-labels-720x560.png": (720, 560),
    "selector/scrolled-720x560.png": (720, 560),
    **{
        f"conversation/composer-ready-{width}x{height}.png": (width, height)
        for width, height in [(720, 560), (900, 700), (1280, 820), (1600, 900)]
    },
    "conversation/composer-mismatch-720x560.png": (720, 560),
    "conversation/composer-keyboard-720x560.png": (720, 560),
    "conversation/configuration-gate-900x700.png": (900, 700),
    **{
        f"models/empty-{width}x{height}.png": (width, height)
        for width, height in [(720, 560), (900, 700), (1280, 820), (1600, 900)]
    },
    "models/keyboard-720x560.png": (720, 560),
    "models/populated-use-settings-900x700.png": (900, 700),
    "models/scrolled-720x560.png": (720, 560),
    "models/provider-running-900x700.png": (900, 700),
}


def verify(root: Path) -> None:
    actual = {path.relative_to(root).as_posix() for path in root.rglob("*.png")}
    if actual != set(EXPECTED):
        raise ValueError(
            f"UI inventory mismatch: missing={sorted(set(EXPECTED) - actual)}, "
            f"unexpected={sorted(actual - set(EXPECTED))}"
        )
    for relative, dimensions in EXPECTED.items():
        path = root / relative
        if path.is_symlink() or path.with_suffix(".png.sha256").is_symlink():
            raise ValueError(f"Symbolic links are not UI artifacts: {relative}")
        content = path.read_bytes()
        if len(content) < 24 or content[:8] != b"\x89PNG\r\n\x1a\n" or content[12:16] != b"IHDR":
            raise ValueError(f"Invalid PNG: {relative}")
        if struct.unpack(">II", content[16:24]) != dimensions:
            raise ValueError(f"Unexpected viewport dimensions: {relative}")
        sidecar = path.with_suffix(".png.sha256").read_text(encoding="utf-8").strip()
        expected_sidecar = hashlib.sha256(content).hexdigest() + "  " + path.name
        if sidecar.lower() != expected_sidecar.lower():
            raise ValueError(f"SHA-256 sidecar mismatch: {relative}")


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: ui-artifacts.py ARTIFACT_DIRECTORY", file=sys.stderr)
        return 2
    try:
        verify(Path(sys.argv[1]))
    except (OSError, ValueError) as error:
        print(f"[ERROR] {error}", file=sys.stderr)
        return 1
    print(f"[OK] UI artifacts: {len(EXPECTED)}/{len(EXPECTED)} PNGs, matching viewports and SHA-256 sidecars.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
