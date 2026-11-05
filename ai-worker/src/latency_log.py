"""บันทึก timestamp ของการทดลองที่ 4 (End-to-End Latency) — ปิดเป็นค่าเริ่มต้น

เปิดด้วย env LATENCY_LOG=true ทั้ง 3 module (ai-worker, backend, Unity) เขียน CSV
รูปแบบเดียวกัน `trackId,stage,epochMs` แล้วไปรวมกันทีหลังด้วย src.analyze_latency

**ทำไมไม่ใส่เวลาลงใน payload:** ถ้าเพิ่ม field ต้องแก้ docs/data-contract.md ซึ่งเป็นสัญญา
ระหว่าง 3 module — การทดลองชั่วคราวไม่คุ้มที่จะแตะ contract จึง join ด้วย trackId แทน

ใช้ time.time() (wall clock) ไม่ใช่ monotonic เพราะต้องเอาเวลาไปเทียบข้าม process/ภาษา
ทุก module จึงต้องรันบนเครื่องเดียวกัน (หรือ sync นาฬิกาด้วย NTP)
"""

from __future__ import annotations

import csv
import os
import time
from pathlib import Path
from typing import Any, TextIO

LATENCY_LOG_ENV = "LATENCY_LOG"
LATENCY_COLUMNS = ["trackId", "stage", "epochMs"]
MS_PER_SEC = 1000.0

# ชื่อ stage ต้องตรงกับ backend/src/latency/latencyLog.ts และ Unity LatencyLog.cs
STAGE_FRAME_READ = "aiFrameRead"  # T1 — อ่านเฟรมที่รถข้ามเส้นเสร็จ (ก่อน YOLO)
STAGE_CROSSING = "aiCrossing"  # ตัวนับยืนยันว่าข้ามเส้น (หลัง YOLO + tracking)
STAGE_POST_START = "aiPostStart"  # T2 — ก่อนเริ่ม HTTP POST


def now_ms() -> float:
    return time.time() * MS_PER_SEC


def is_enabled(environ: dict[str, str] | None = None) -> bool:
    value = (environ if environ is not None else os.environ).get(LATENCY_LOG_ENV, "")
    return value.strip().lower() in {"1", "true", "yes"}


class LatencyLog:
    """เขียน 1 แถวต่อ 1 stage — ปิดอยู่ = ทุก method ไม่ทำอะไร จึงเรียกได้ทุกที่ไม่ต้องเช็คเอง"""

    def __init__(self, path: Path, enabled: bool) -> None:
        self.path = path
        self.enabled = enabled
        self._handle: TextIO | None = None
        self._writer: Any = None  # csv.writer ไม่มี type สาธารณะให้อ้าง

    def open(self) -> None:
        """เขียนทับไฟล์เดิม — ผลของหลายรอบปนกันจะ join ผิดเพราะ trackId ซ้ำได้"""
        if not self.enabled:
            return
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._handle = self.path.open("w", newline="", encoding="utf-8")
        self._writer = csv.writer(self._handle)
        self._writer.writerow(LATENCY_COLUMNS)
        self._handle.flush()

    def record(self, track_id: str, stage: str, epoch_ms: float) -> None:
        if self._writer is None or self._handle is None:
            return
        self._writer.writerow([track_id, stage, f"{epoch_ms:.3f}"])
        # flush ทุกแถว: กด Ctrl-C กลางทดลองแล้วข้อมูลที่เก็บมาต้องไม่หาย
        self._handle.flush()

    def close(self) -> None:
        if self._handle is not None:
            self._handle.close()
            self._handle = None
            self._writer = None
