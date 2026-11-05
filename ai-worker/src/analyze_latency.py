"""การทดลองที่ 4 — รวม timestamp จาก 3 module แล้วคำนวณ latency ต่อ segment

    python -m src.analyze_latency
    python -m src.analyze_latency --ai a.csv --backend b.csv --unity c.csv

แต่ละ module เขียน `trackId,stage,epochMs` ของตัวเอง (เปิดด้วย LATENCY_LOG=true / ช่อง
Log Latency ใน Unity) สคริปต์นี้ join ด้วย trackId — event ที่ขาด stage ไหนก็ตามจะถูกตัด
ออกและรายงานจำนวน ไม่เงียบ

**trackId ซ้ำข้ามรอบได้** (รันคลิปเดิมซ้ำโดยไม่รีสตาร์ต backend/Unity) จึงไม่ join ด้วย
"แถวแรก" แต่ไล่ตามลำดับเวลา: แต่ละ stage เลือกค่าแรกที่ไม่เกิดก่อน stage ก่อนหน้า
— เจอจริง 2026-11-06: เลือกแถวแรกแล้วได้ Segment 2 = -699,203 ms แต่ E2E ดันขึ้น PASS

Segment ที่รายงาน (T = ชื่อในเอกสารขั้นตอนการทดลอง):
  1 AI Processing      T2 - T1   aiPostStart  - aiFrameRead   (รวม YOLO + tracking + นับ)
  2 HTTP POST Ingest   T3 - T2   beValidated  - aiPostStart
  3 WS Broadcast       T4 - T3   beEmit       - beValidated
  4 Unity Receive      T5 - T4   unityReceived - beEmit
  E2E                  T5 - T1   unityReceived - aiFrameRead
และอีก 2 แถวประกอบ (ไม่ใช่เกณฑ์):
  - YOLO+tracking      aiCrossing - aiFrameRead   ส่วนย่อยของ segment 1
  - Spawn queue wait   unitySpawned - unityReceived  คิว spawn ของ ScenarioManager
    (หน่วง networkSpawnDelay + รอเลนว่าง โดยตั้งใจ — เป็นการออกแบบภาพ ไม่ใช่ latency ของสาย)
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import Counter
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path

from src.latency_log import STAGE_CROSSING, STAGE_FRAME_READ, STAGE_POST_START

HERE = Path(__file__).parent
REPO_ROOT = HERE.parent.parent
DEFAULT_AI_PATH = HERE.parent / "data" / "output_results" / "latency_ai.csv"
DEFAULT_BACKEND_PATH = REPO_ROOT / "backend" / "data" / "output" / "latency_backend.csv"
DEFAULT_UNITY_PATH = REPO_ROOT / "unity" / "Smartflow" / "Logs" / "latency_unity.csv"
DEFAULT_MERGED_PATH = HERE.parent / "data" / "output_results" / "latency_merged.csv"

# ต้องตรงกับ backend/src/latency/latencyLog.ts และ Unity LatencyLog.cs
STAGE_VALIDATED = "beValidated"
STAGE_EMIT = "beEmit"
STAGE_UNITY_RECEIVED = "unityReceived"
STAGE_UNITY_SPAWNED = "unitySpawned"

REQUIRED_STAGES = (
    STAGE_FRAME_READ,
    STAGE_POST_START,
    STAGE_VALIDATED,
    STAGE_EMIT,
    STAGE_UNITY_RECEIVED,
)

E2E_TARGET_MS = 1000.0
MIN_EVENTS = 30  # เกณฑ์ขั้นต่ำในเอกสารขั้นตอนการทดลอง
MEDIAN_PCT = 50.0
P95_PCT = 95.0
# Date.now() / ToUnixTimeMilliseconds() ปัดลงเป็น ms เต็ม แต่ Python มีเศษ ms
# stage ถัดไปจึงดูเหมือนเกิดก่อนได้ไม่เกิน 1 ms ทั้งที่จริงเกิดทีหลัง
TIMER_RESOLUTION_MS = 1.0

RawLog = dict[str, dict[str, list[float]]]
Timeline = dict[str, dict[str, float]]

# ลำดับที่เวลาต้องเดินหน้า — (stage, stage ที่ต้องเกิดก่อน)
STAGE_CHAIN: tuple[tuple[str, str | None], ...] = (
    (STAGE_FRAME_READ, None),
    (STAGE_CROSSING, STAGE_FRAME_READ),
    (STAGE_POST_START, STAGE_FRAME_READ),
    (STAGE_VALIDATED, STAGE_POST_START),
    (STAGE_EMIT, STAGE_VALIDATED),
    (STAGE_UNITY_RECEIVED, STAGE_EMIT),
    (STAGE_UNITY_SPAWNED, STAGE_UNITY_RECEIVED),
)


@dataclass(frozen=True)
class Segment:
    label: str
    start: str
    end: str
    is_extra: bool = False


SEGMENTS = (
    Segment("Segment 1 - AI Processing", STAGE_FRAME_READ, STAGE_POST_START),
    Segment("Segment 2 - HTTP POST Ingest", STAGE_POST_START, STAGE_VALIDATED),
    Segment("Segment 3 - WS Broadcast", STAGE_VALIDATED, STAGE_EMIT),
    Segment("Segment 4 - Unity Receive", STAGE_EMIT, STAGE_UNITY_RECEIVED),
    Segment("E2E Total", STAGE_FRAME_READ, STAGE_UNITY_RECEIVED),
    Segment("  (in seg 1) YOLO+tracking", STAGE_FRAME_READ, STAGE_CROSSING, is_extra=True),
    Segment("  Spawn queue wait (extra)", STAGE_UNITY_RECEIVED, STAGE_UNITY_SPAWNED, True),
)


def load_stages(path: Path, raw: RawLog) -> int:
    """อ่าน CSV เข้า raw[trackId][stage] (เก็บทุกค่า) — คืนจำนวนแถวที่ trackId+stage ซ้ำ"""
    duplicates = 0
    with path.open(newline="", encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            values = raw.setdefault(row["trackId"], {}).setdefault(row["stage"], [])
            if values:
                duplicates += 1
            values.append(float(row["epochMs"]))
    return duplicates


def resolve_event(stages: dict[str, list[float]]) -> dict[str, float]:
    """เลือก 1 ค่าต่อ stage ให้เวลาเดินหน้าเสมอ — ค่าของรอบเก่าที่เกิดก่อนจะถูกข้ามไปเอง"""
    chosen: dict[str, float] = {}
    for stage, after in STAGE_CHAIN:
        if after is not None and after not in chosen:
            continue
        floor = chosen[after] - TIMER_RESOLUTION_MS if after is not None else float("-inf")
        candidates = sorted(v for v in stages.get(stage, []) if v >= floor)
        if candidates:
            chosen[stage] = candidates[0]
    return chosen


def complete_events(raw: RawLog) -> Timeline:
    events: Timeline = {}
    for track_id, stages in raw.items():
        chosen = resolve_event(stages)
        if all(stage in chosen for stage in REQUIRED_STAGES):
            events[track_id] = chosen
    return events


def percentile(values: Sequence[float], pct: float) -> float:
    """linear interpolation แบบเดียวกับ numpy.percentile ค่าเริ่มต้น — ไม่ต้องพึ่ง numpy"""
    if not values:
        raise ValueError("percentile of empty data")
    ordered = sorted(values)
    rank = (len(ordered) - 1) * pct / 100.0
    low = int(rank)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (rank - low)


def segment_durations(events: Timeline, segment: Segment) -> list[float]:
    return [
        stages[segment.end] - stages[segment.start]
        for stages in events.values()
        if segment.start in stages and segment.end in stages
    ]


def write_merged(path: Path, events: Timeline) -> None:
    """ไฟล์ดิบต่อ event ไว้แนบภาคผนวก / ตรวจย้อนหลัง"""
    stage_columns = [*REQUIRED_STAGES, STAGE_CROSSING, STAGE_UNITY_SPAWNED]
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["trackId", *stage_columns])
        for track_id in sorted(events):
            stages = events[track_id]
            writer.writerow([track_id, *(stages.get(s, "") for s in stage_columns)])


def negative_segments(events: Timeline) -> list[str]:
    """เวลาย้อนกลับ = join ผิดหรือนาฬิกาไม่ตรงกัน ผลทั้งชุดเชื่อไม่ได้ ห้ามรายงานว่าผ่าน"""
    problems = []
    for segment in SEGMENTS:
        worst = min(segment_durations(events, segment), default=0.0)
        if worst < -TIMER_RESOLUTION_MS:
            problems.append(f"{segment.label.strip()}: {worst:.1f} ms")
    return problems


def print_report(events: Timeline) -> bool:
    """พิมพ์ตาราง + คืน True ถ้า E2E median ผ่านเกณฑ์"""
    print(f"{'':32}{'n':>5}{'Median (ms)':>14}{'P95 (ms)':>12}")
    e2e_median = float("inf")
    for segment in SEGMENTS:
        durations = segment_durations(events, segment)
        if not durations:
            print(f"{segment.label:32}{0:>5}{'-':>14}{'-':>12}")
            continue
        median = percentile(durations, MEDIAN_PCT)
        p95 = percentile(durations, P95_PCT)
        print(f"{segment.label:32}{len(durations):>5}{median:>14.1f}{p95:>12.1f}")
        if segment.end == STAGE_UNITY_RECEIVED and segment.start == STAGE_FRAME_READ:
            e2e_median = median

    passed = e2e_median <= E2E_TARGET_MS
    verdict = "PASS" if passed else "FAIL"
    print(f"\nE2E Median <= {E2E_TARGET_MS:.0f} ms : {verdict} ({e2e_median:.1f} ms)")
    return passed


def print_dropped(raw: RawLog, events: Timeline) -> None:
    """นับเฉพาะ event ของรอบนี้ (มีเวลาจาก AI) — trackId ที่มีแต่ใน log รอบเก่าไม่ใช่การตกหล่น

    บอกว่าขาด stage ไหน เพราะสาเหตุต่างกันมาก: ขาด unityReceived ทั้งท้ายคลิป = Unity หยุด
    ก่อน AI รันจบ ส่วนขาด beValidated = backend ปฏิเสธ/ล่ม
    """
    missing: Counter[str] = Counter()
    for track_id, stages in raw.items():
        if track_id in events or STAGE_FRAME_READ not in stages:
            continue
        chosen = resolve_event(stages)
        first_gap = next(stage for stage in REQUIRED_STAGES if stage not in chosen)
        missing[first_gap] += 1
    if not missing:
        print()
        return
    detail = ", ".join(f"{n} stop before {stage}" for stage, n in missing.most_common())
    print(f"[latency] {sum(missing.values())} incomplete dropped ({detail})\n")


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Experiment 4: end-to-end latency per segment")
    parser.add_argument("--ai", type=Path, default=DEFAULT_AI_PATH)
    parser.add_argument("--backend", type=Path, default=DEFAULT_BACKEND_PATH)
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY_PATH)
    parser.add_argument("--out", type=Path, default=DEFAULT_MERGED_PATH)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    raw: RawLog = {}
    for path in (args.ai, args.backend, args.unity):
        if not path.exists():
            print(f"[error] Latency log not found: {path}", file=sys.stderr)
            print("        Did you set LATENCY_LOG=true / tick Log Latency?", file=sys.stderr)
            return 1
        duplicates = load_stages(path, raw)
        if duplicates:
            # trackId ซ้ำ = log ของหลายรอบปนกัน — จับคู่ตามเวลาได้ แต่ควรรีสตาร์ตให้ครบ
            print(
                f"[warn] {path.name}: {duplicates} repeated rows (earlier runs?) - "
                "matched by time order; restart all 3 modules for a clean run"
            )

    events = complete_events(raw)
    print(f"[latency] {len(events)} complete events")
    print_dropped(raw, events)
    if not events:
        print("[error] No event has all stages — nothing to analyze", file=sys.stderr)
        return 1
    if len(events) < MIN_EVENTS:
        print(f"[warn] Fewer than {MIN_EVENTS} events — the protocol asks for at least that\n")

    problems = negative_segments(events)
    if problems:
        print("[error] Time runs backwards - results are not trustworthy:", file=sys.stderr)
        for problem in problems:
            print(f"        {problem}", file=sys.stderr)
        print("        Are all modules on the same machine / clock?", file=sys.stderr)
        return 3

    passed = print_report(events)
    write_merged(args.out, events)
    print(f"\nPer-event timestamps: {args.out}")
    return 0 if passed else 2


if __name__ == "__main__":
    raise SystemExit(main())
