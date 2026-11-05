"""เทส latency_log.py + analyze_latency.py — ใช้ไฟล์ชั่วคราว ไม่ต้องรัน pipeline จริง"""

from pathlib import Path

import pytest

from src.analyze_latency import (
    SEGMENTS,
    complete_events,
    load_stages,
    main,
    negative_segments,
    percentile,
    segment_durations,
)
from src.latency_log import LatencyLog, is_enabled


def test_is_enabled_reads_truthy_values_only():
    assert is_enabled({"LATENCY_LOG": "true"})
    assert is_enabled({"LATENCY_LOG": " 1 "})
    assert not is_enabled({"LATENCY_LOG": "false"})
    assert not is_enabled({})


def test_disabled_log_writes_nothing(tmp_path: Path):
    log = LatencyLog(tmp_path / "x.csv", enabled=False)
    log.open()
    log.record("car-0001", "aiFrameRead", 1.0)
    log.close()
    assert not (tmp_path / "x.csv").exists()


def test_percentile_matches_numpy_linear():
    values = [10.0, 20.0, 30.0, 40.0]
    assert percentile(values, 50) == 25.0
    assert percentile(values, 95) == pytest.approx(38.5)
    assert percentile([7.0], 95) == 7.0


def test_percentile_rejects_empty():
    with pytest.raises(ValueError):
        percentile([], 50)


def write_csv(path: Path, rows: list[tuple[str, str, float]]) -> Path:
    lines = ["trackId,stage,epochMs", *(f"{t},{s},{ms}" for t, s, ms in rows)]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def make_logs(tmp_path: Path) -> tuple[Path, Path, Path]:
    ai = write_csv(
        tmp_path / "ai.csv",
        [
            ("car-0001", "aiFrameRead", 1000),
            ("car-0001", "aiCrossing", 1040),
            ("car-0001", "aiPostStart", 1050),
            ("car-0002", "aiFrameRead", 2000),
            ("car-0002", "aiPostStart", 2060),
        ],
    )
    backend = write_csv(
        tmp_path / "be.csv",
        [
            ("car-0001", "beValidated", 1055),
            ("car-0001", "beEmit", 1056),
            ("car-0002", "beValidated", 2070),
            ("car-0002", "beEmit", 2071),
        ],
    )
    # car-0002 ไม่ถึง Unity -> ต้องถูกตัดออก ไม่ใช่นับเป็น 0
    unity = write_csv(
        tmp_path / "unity.csv",
        [("car-0001", "unityReceived", 1080), ("car-0001", "unitySpawned", 1600)],
    )
    return ai, backend, unity


def test_join_drops_incomplete_and_computes_segments(tmp_path: Path):
    raw: dict[str, dict[str, list[float]]] = {}
    for path in make_logs(tmp_path):
        load_stages(path, raw)
    events = complete_events(raw)

    assert list(events) == ["car-0001"]
    by_label = {s.label: segment_durations(events, s) for s in SEGMENTS}
    assert by_label["Segment 1 - AI Processing"] == [50]
    assert by_label["Segment 2 - HTTP POST Ingest"] == [5]
    assert by_label["Segment 3 - WS Broadcast"] == [1]
    assert by_label["Segment 4 - Unity Receive"] == [24]
    assert by_label["E2E Total"] == [80]


def test_duplicate_rows_are_counted(tmp_path: Path):
    path = write_csv(
        tmp_path / "dup.csv",
        [("car-0001", "beEmit", 10), ("car-0001", "beEmit", 99)],
    )
    raw: dict[str, dict[str, list[float]]] = {}
    assert load_stages(path, raw) == 1
    assert raw["car-0001"]["beEmit"] == [10, 99]


def test_stale_rows_from_earlier_run_are_skipped_by_time_order():
    """backend ไม่ได้รีสตาร์ต: แถวรอบเก่าของ trackId เดียวกันเกิดก่อน T2 ต้องไม่ถูกเลือก"""
    raw = {
        "car-0011": {
            "aiFrameRead": [10_000.0],
            "aiPostStart": [10_150.0],
            "beValidated": [1_000.0, 10_153.0],  # 1_000 = รอบเก่า
            "beEmit": [1_001.0, 10_154.0],
            "unityReceived": [10_200.0],
        }
    }
    events = complete_events(raw)
    assert events["car-0011"]["beValidated"] == 10_153.0
    assert negative_segments(events) == []


def test_sub_millisecond_rounding_is_tolerated():
    """Node ปัด ms ลง — beValidated ดูก่อน aiPostStart ได้ไม่เกิน 1 ms"""
    raw = {
        "car-0001": {
            "aiFrameRead": [0.0],
            "aiPostStart": [100.6],
            "beValidated": [100.0],
            "beEmit": [101.0],
            "unityReceived": [120.0],
        }
    }
    assert "car-0001" in complete_events(raw)


def test_backwards_time_is_flagged():
    events = {
        "car-0001": {
            "aiFrameRead": 0.0,
            "aiPostStart": 100.0,
            "beValidated": 50.0,
            "beEmit": 51.0,
            "unityReceived": 60.0,
        }
    }
    assert any("Segment 2" in p for p in negative_segments(events))


def test_main_passes_and_writes_merged(tmp_path: Path):
    ai, backend, unity = make_logs(tmp_path)
    out = tmp_path / "merged.csv"
    code = main(
        ["--ai", str(ai), "--backend", str(backend), "--unity", str(unity), "--out", str(out)]
    )
    assert code == 0
    assert out.read_text(encoding="utf-8").splitlines()[1].startswith("car-0001,")


def test_main_fails_loudly_when_log_missing(tmp_path: Path):
    ai, backend, _ = make_logs(tmp_path)
    missing = tmp_path / "nope.csv"
    assert main(["--ai", str(ai), "--backend", str(backend), "--unity", str(missing)]) == 1
