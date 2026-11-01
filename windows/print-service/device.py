"""WinUSB / libusb access for CP-9550DW-S."""

from __future__ import annotations

import sys
import time
from dataclasses import dataclass

import usb.core
import usb.util

from protocol import (
    CLEAR_MEMORY_S,
    CMD_MEDIA,
    CMD_SERIAL,
    CMD_STATUS,
    CMD_STATUS_B,
    FOOTER_9550S,
    S_INIT,
    USB_PID_CP9550DW_S,
    USB_VID,
    iter_chunks,
    parse_media_status,
)

try:
    # Prefer the bundled libusb on Windows so users need no system DLL hunt.
    import libusb_package

    _backend = libusb_package.get_libusb1_backend()
except Exception:  # noqa: BLE001 — optional Windows helper
    _backend = None


@dataclass
class PrinterInfo:
    serial: str
    media_name: str
    media_remain: int
    media_max: int


class Cp9550S:
    def __init__(self) -> None:
        kwargs = {"idVendor": USB_VID, "idProduct": USB_PID_CP9550DW_S}
        if _backend is not None:
            kwargs["backend"] = _backend
        self.dev = usb.core.find(**kwargs)
        if self.dev is None:
            raise RuntimeError(
                "CP-9550DW-S not found (USB 06D3:03A5). "
                "Install windows/inf/CP9550DW-S.inf or bind WinUSB with Zadig."
            )
        self._claim()

    def _claim(self) -> None:
        self.dev.set_configuration()
        cfg = self.dev.get_active_configuration()
        intf = cfg[(0, 0)]
        if self.dev.is_kernel_driver_active(intf.bInterfaceNumber):
            try:
                self.dev.detach_kernel_driver(intf.bInterfaceNumber)
            except Exception:  # noqa: BLE001
                pass
        usb.util.claim_interface(self.dev, intf.bInterfaceNumber)
        self.ep_out = usb.util.find_descriptor(
            intf,
            custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress)
            == usb.util.ENDPOINT_OUT,
        )
        self.ep_in = usb.util.find_descriptor(
            intf,
            custom_match=lambda e: usb.util.endpoint_direction(e.bEndpointAddress)
            == usb.util.ENDPOINT_IN,
        )
        if self.ep_out is None or self.ep_in is None:
            raise RuntimeError("Could not locate bulk endpoints on CP-9550DW-S")

    def close(self) -> None:
        try:
            usb.util.dispose_resources(self.dev)
        except Exception:  # noqa: BLE001
            pass

    def write(self, data: bytes, timeout: int = 15000) -> None:
        self.ep_out.write(data, timeout=timeout)

    def read(self, size: int = 128, timeout: int = 5000) -> bytes:
        return bytes(self.ep_in.read(size, timeout=timeout))

    def query(self, cmd: bytes, size: int = 64) -> bytes:
        self.write(cmd)
        return self.read(size)

    def s_handshake(self) -> bytes:
        self.write(S_INIT[0])
        self.write(S_INIT[1])
        return self.read(16)

    def media_status(self) -> dict:
        raw = self.query(CMD_MEDIA, 64)
        parsed = parse_media_status(raw)
        if not parsed:
            raise RuntimeError(f"Unexpected media response: {raw[:16].hex()}")
        return parsed

    def status(self) -> bytes:
        return self.query(CMD_STATUS, 64)

    def status_b(self) -> bytes:
        return self.query(CMD_STATUS_B, 64)

    def serial(self) -> str:
        raw = self.query(CMD_SERIAL, 128)
        # Response is UTF-16LE after a small binary header.
        if len(raw) < 8:
            return ""
        length = raw[4]
        payload = raw[5 : 5 + length]
        try:
            return payload.decode("utf-16-le", errors="ignore").strip()
        except Exception:  # noqa: BLE001
            return raw.hex()

    def info(self) -> PrinterInfo:
        media = self.media_status()
        return PrinterInfo(
            serial=self.serial(),
            media_name=media["name"],
            media_remain=media["remain"],
            media_max=media["max"],
        )

    def wait_idle(self, timeout_s: float = 180.0) -> None:
        deadline = time.time() + timeout_s
        while time.time() < deadline:
            sts = self.status()
            # sts5 at offset 16 — 0 means ready for another job (per selphy_print).
            sts5 = sts[16] if len(sts) > 16 else 0xFF
            if sts5 == 0:
                return
            time.sleep(1.0)
        raise TimeoutError("Printer did not return to idle in time")

    def send_spool_payload(self, spool: bytes, progress=None) -> None:
        """
        Send a complete .mitsu9550s spool with -S handshake and clear-memory.
        Spool layout: headers (150) + planes + footer.
        """
        if len(spool) < 154:
            raise ValueError("Spool too small")
        if spool[0:4] != bytes([0x1B, 0x57, 0x20, 0x2E]):
            raise ValueError("Not a CP-9550 header-1 spool")

        headers = spool[:150]
        body = spool[150:]
        if body.endswith(FOOTER_9550S):
            planes = body[: -len(FOOTER_9550S)]
        else:
            planes = body

        self.s_handshake()
        self.media_status()
        self.status_b()
        self.wait_idle(timeout_s=30)

        self.write(headers)
        self.write(CLEAR_MEMORY_S)

        sent = 0
        for chunk in iter_chunks(planes, 262144):
            self.write(chunk, timeout=60000)
            sent += len(chunk)
            if progress:
                progress(sent, len(planes))

        self.media_status()
        self.status_b()
        sts = self.status()
        if len(sts) > 7 and sts[6] != 0:
            raise RuntimeError("Printer cover open (or status MM busy/error)")

        self.write(FOOTER_9550S)
        if progress:
            progress(len(planes), len(planes))


def open_printer() -> Cp9550S:
    return Cp9550S()


def main_status() -> int:
    try:
        prn = open_printer()
    except Exception as exc:  # noqa: BLE001
        print(exc, file=sys.stderr)
        return 1
    try:
        info = prn.info()
        print(f"Model/serial : {info.serial or '(unknown)'}")
        print(f"Media        : {info.media_name}")
        print(f"Remaining    : {info.media_remain} / {info.media_max}")
        return 0
    finally:
        prn.close()


if __name__ == "__main__":
    raise SystemExit(main_status())
