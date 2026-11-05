import fs from 'node:fs';
import path from 'node:path';

/**
 * บันทึก timestamp ของการทดลองที่ 4 (End-to-End Latency) — ปิดเป็นค่าเริ่มต้น
 *
 * เปิดด้วย LATENCY_LOG=true ทุก module เขียน CSV รูปแบบเดียวกัน `trackId,stage,epochMs`
 * แล้วไป join ด้วย trackId ใน ai-worker/src/analyze_latency.py
 *
 * ไม่ใส่เวลาลง payload เพราะต้องแก้ docs/data-contract.md — การทดลองชั่วคราวไม่คุ้มแตะ contract
 */

const OUTPUT_DIR = path.join(process.cwd(), 'data', 'output');
export const LATENCY_FILE = path.join(OUTPUT_DIR, 'latency_backend.csv');
const LATENCY_HEADER = 'trackId,stage,epochMs\n';

// ต้องตรงกับ ai-worker/src/latency_log.py และ Unity LatencyLog.cs
export const STAGE_VALIDATED = 'beValidated'; // T3
export const STAGE_EMIT = 'beEmit'; // T4

export function isLatencyLogEnabled(): boolean {
  const value = (process.env['LATENCY_LOG'] ?? '').trim().toLowerCase();
  return value === 'true' || value === '1' || value === 'yes';
}

/** ห้าม throw — การบันทึกล้มเหลวต้องไม่ทำให้ broadcast หยุด แต่ต้อง log ให้รู้ว่าไฟล์ไม่ครบ */
export function recordLatency(trackId: string, stage: string, epochMs: number): void {
  if (!isLatencyLogEnabled()) return;

  // trackId มาจาก client — กัน comma/ขึ้นบรรทัดใหม่ทำให้ CSV แตกคอลัมน์
  const safeId = trackId.replace(/[,\r\n]/g, '_');
  fs.appendFile(LATENCY_FILE, `${safeId},${stage},${epochMs}\n`, (err) => {
    if (err) console.warn(`[latency] Cannot write ${LATENCY_FILE}: ${err.message}`);
  });
}

/** ล้างไฟล์ตอนเริ่มรอบใหม่ — trackId ซ้ำข้ามรอบได้ ถ้าไม่ล้างการ join จะจับคู่ผิดรอบ */
export function resetLatencyLog(): void {
  if (!isLatencyLogEnabled()) return;

  try {
    fs.mkdirSync(OUTPUT_DIR, { recursive: true });
    fs.writeFileSync(LATENCY_FILE, LATENCY_HEADER);
    console.log(`[latency] Logging timestamps to ${LATENCY_FILE}`);
  } catch (err) {
    console.warn(`[latency] Cannot reset ${LATENCY_FILE}: ${(err as Error).message}`);
  }
}
