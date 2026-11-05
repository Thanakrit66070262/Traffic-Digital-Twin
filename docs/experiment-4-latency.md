# การทดลองที่ 4 — ความหน่วงของการเชื่อมโยงข้อมูล (End-to-End Latency)

> เอกสารนี้อธิบาย **ทำไม** ต้องทดลอง, **วัดอะไร/วัดยังไง**, **ต้องเตรียมอะไร**, **ขั้นตอนรัน**
> และ **ข้อควรระวัง** ที่เจอจากการรันจริง — ใช้คู่กับ `docs/ขั้นตอนการทำการทดลอง.txt` (ข้อ 4)
> และตาราง `tab:latency` ใน `docs/Chapter4.tex`
>
> สร้าง: 2026-11-06

---

## 1. ทำไมต้องทำการทดลองนี้

Smart Flow อ้างว่าเป็น **Digital Twin แบบ real-time** — ภาพ 3D ใน Unity ต้องสะท้อนสิ่งที่
เกิดขึ้นบนถนน "ตอนนี้" ไม่ใช่ "เมื่อสักครู่" ถ้าข้อมูลมาถึงช้า แบบจำลองจะกลายเป็นการเล่นภาพ
ย้อนหลัง ซึ่งเสียความหมายของคำว่า twin ไป การทดลองนี้มีไว้ตอบ 3 คำถาม:

1. **ระบบเร็วพอไหม** — ตรวจเกณฑ์ยอมรับ (Acceptance Criteria) **E2E Median ≤ 1.0 วินาที**
   ตั้งแต่รถข้ามเส้นในวิดีโอจนถึงตอนที่ Unity ได้รับข้อมูลรถคันนั้น
2. **ช้าที่ตรงไหน** — แบ่งเป็น 4 ช่วง (segment) เพื่อหาคอขวด ถ้าจะทำให้เร็วขึ้น (เช่นเป้า
   M3 ใน `project-status.md`: < 500 ms) จะได้รู้ว่าต้องแก้ที่ AI, เครือข่าย, backend หรือ Unity
3. **เสถียรไหม** — รายงานทั้ง **Median** (กรณีทั่วไป) และ **P95** (กรณีแย่ที่ 95% ของเวลา)
   เพราะค่าเฉลี่ยดีอย่างเดียวไม่พอ ถ้ามีบางคันค้างหลายวินาที ผู้ใช้จะเห็นรถโผล่ช้าเป็นระยะ

ผลยังใช้ยืนยันการออกแบบสถาปัตยกรรม (HTTP POST → Backend → Socket.io → Unity) ว่าชั้นที่
เพิ่มเข้ามา (validate + relay) ไม่ได้เป็นภาระด้านเวลาอย่างมีนัยสำคัญ

---

## 2. วัดอะไร

### จุดประทับเวลา (timestamp)

| จุด | ชื่อ stage | อยู่ในไฟล์ | เกิดเมื่อ |
|---|---|---|---|
| **T1** | `aiFrameRead` | `ai-worker/src/main.py` | อ่านเฟรมที่รถข้ามเส้นเสร็จ (ก่อนเข้า YOLO) |
| (ย่อย) | `aiCrossing` | `ai-worker/src/main.py` | YOLO + tracking + ตัวนับยืนยันว่าข้ามเส้นแล้ว |
| **T2** | `aiPostStart` | `ai-worker/src/main.py` | ก่อนเริ่ม HTTP POST ไป backend |
| **T3** | `beValidated` | `backend/src/controllers/ingestController.ts` | ผ่าน `validateSpawnEvent()` แล้ว |
| **T4** | `beEmit` | `backend/src/controllers/ingestController.ts` | ก่อน `io.emit('spawn_vehicle')` |
| **T5** | `unityReceived` | `unity/.../websocket/SpawnVehicleTestClient.cs` | Unity รับ event + parse JSON เสร็จ |
| (เสริม) | `unitySpawned` | `unity/.../ScenarioManager.cs` | รถออกจาก Object Pool มาอยู่บนถนน |

### Segment ที่รายงาน

| Segment | สูตร | ความหมาย |
|---|---|---|
| 1 — AI Processing | T2 − T1 | YOLO + tracking + นับ + สร้าง payload |
| 2 — HTTP POST Ingest | T3 − T2 | ส่ง HTTP + Express รับ + validate |
| 3 — WS Broadcast | T4 − T3 | อัปเดตตัวนับ + เตรียม payload สำหรับ Unity |
| 4 — Unity Receive | T5 − T4 | Socket.io ส่ง → Unity รับ → ส่งเข้า main thread → parse |
| **E2E** | **T5 − T1** | **ค่าที่ใช้ตัดสินเกณฑ์** |
| (ย่อย) YOLO+tracking | `aiCrossing` − T1 | ส่วนของ Segment 1 ที่เป็นโมเดล |
| (เสริม) Spawn queue wait | `unitySpawned` − T5 | เวลารอคิว spawn ใน Unity — **ไม่นับรวม E2E** (ดู §3) |

---

## 3. การตัดสินใจในการออกแบบ (และเหตุผล)

### 3.1 log แยกแต่ละ module แล้ว join ด้วย `trackId` — ไม่ใส่เวลาลง payload

เอกสารขั้นตอนเดิมให้เพิ่ม field `ai_sent_at` ใน payload แต่:
- ต้องแก้ `docs/data-contract.md` ซึ่งเป็นสัญญาระหว่าง 3 module (กฎข้อ 1 ใน `CLAUDE.md`)
- ชื่อเป็น snake_case ขัดกับกฎ JSON ต้องเป็น camelCase

จึงให้แต่ละ module เขียน CSV ของตัวเองรูปแบบเดียวกัน `trackId,stage,epochMs` แล้วรวมทีหลัง
→ **data contract ไม่เปลี่ยน**, ปิดเป็นค่าเริ่มต้น ไม่กระทบการใช้งานปกติ

### 3.2 T1 = เวลาอ่านเฟรม ไม่ใช่เวลาที่ "ตรวจพบการข้ามเส้น"

ถ้าใช้เวลาที่ตัวนับยืนยันการข้าม Segment 1 จะไม่รวม YOLO และเหลือ ~0 ms ซึ่งไม่สะท้อน
"AI Processing" จริง จึงใช้เวลาอ่านเฟรมเป็น T1 และเก็บเวลาตรวจพบไว้เป็นแถวย่อยแทน

### 3.3 T5 = เวลา Unity "รับ" event ไม่ใช่เวลารถ "โผล่"

`ScenarioManager` มีคิว spawn ที่ **ตั้งใจหน่วง** `networkSpawnDelay` (0.5 วินาที/คัน) และรอ
ให้เลนว่างก่อนวางรถ (กันรถซ้อนกัน) ซึ่งเป็นการออกแบบด้านภาพ ไม่ใช่ความหน่วงของสายข้อมูล
ถ้านับรวมจะทำให้ตัวเลขสะท้อนการตั้งค่าแอนิเมชันมากกว่าประสิทธิภาพระบบ จึงรายงานแยกเป็น
"Spawn queue wait" — ถ้าอาจารย์ต้องการนับถึงตอนรถโผล่ ให้ใช้ `unitySpawned` แทน (แก้ 1 บรรทัด
ใน `SEGMENTS` ของ `analyze_latency.py`)

### 3.4 นาฬิกา

ทุก module ใช้ **wall clock หน่วย ms นับจาก Unix epoch** (Python `time.time()`, Node
`Date.now()`, C# `DateTimeOffset.UtcNow`) เพราะต้องเทียบข้าม process/ภาษา → **ต้องรันทุกตัว
บนเครื่องเดียวกัน** (ถ้าคนละเครื่องต้อง sync ด้วย NTP และความคลาดของ NTP จะปนเข้าไปในผล)

Node และ Unity ปัดเวลาลงเป็น ms เต็ม แต่ Python มีเศษ ms → สคริปต์วิเคราะห์ยอมให้ stage
ถัดไป "ดูเหมือนเกิดก่อน" ได้ไม่เกิน 1 ms

### 3.5 การเขียน log ไม่ปนเวลาที่วัด

ทุกจุดจับเวลาเก็บไว้ในตัวแปรก่อน แล้วค่อยเขียนไฟล์ **หลัง** การทำงานที่ถูกวัด (เช่น เขียน
`latency_ai.csv` หลัง POST เสร็จ, backend เขียนหลัง emit)

---

## 4. ไฟล์ที่เกี่ยวข้อง

| ไฟล์ | หน้าที่ |
|---|---|
| `ai-worker/src/latency_log.py` | ตัวเขียน CSV ฝั่ง AI + ชื่อ stage |
| `ai-worker/src/main.py` | จับ T1, `aiCrossing`, T2 · flag `--latency-log` |
| `ai-worker/src/analyze_latency.py` | รวม 3 ไฟล์ → Median/P95 → PASS/FAIL |
| `ai-worker/tests/test_latency.py` | unit test ของการ join/percentile/การตรวจเวลาย้อนกลับ |
| `backend/src/latency/latencyLog.ts` | ตัวเขียน CSV ฝั่ง backend |
| `backend/src/controllers/ingestController.ts` | จับ T3, T4 |
| `unity/Smartflow/Assets/Scripts/websocket/LatencyLog.cs` | ตัวเขียน CSV ฝั่ง Unity |
| `unity/.../websocket/SpawnVehicleTestClient.cs` | จับ T5 · ช่อง **Log Latency** ใน Inspector |
| `unity/.../ScenarioManager.cs` | จับ `unitySpawned` |

ไฟล์ผลลัพธ์ (ทั้งหมดอยู่ใน path ที่ gitignore แล้ว):

| ไฟล์ | สร้างโดย |
|---|---|
| `ai-worker/data/output_results/latency_ai.csv` | AI Worker |
| `backend/data/output/latency_backend.csv` | Backend |
| `unity/Smartflow/Logs/latency_unity.csv` | Unity (Editor) |
| `ai-worker/data/output_results/latency_merged.csv` | สคริปต์วิเคราะห์ — เวลาทุก stage ต่อคัน ใช้แนบภาคผนวก |

---

## 5. สิ่งที่ต้องเตรียม

**ฮาร์ดแวร์ / สภาพแวดล้อม**
- [ ] เครื่องเดียวรันครบทั้ง 3 module (AI Worker, Backend, Unity Editor)
- [ ] ปิดโปรแกรมหนัก ๆ อื่น — latency แปรตาม CPU/GPU load
- [ ] จดสเปกเครื่อง (CPU, GPU, RAM) ไว้ใส่ในรายงาน เพราะ Segment 1 ขึ้นกับ GPU โดยตรง

**AI Worker**
- [ ] วิดีโอทดสอบที่มีรถข้ามเส้น **≥ 30 คัน** (แนะนำ ~50 คันขึ้นไป เพื่อให้ P95 เชื่อถือได้)
- [ ] ไฟล์ config ใน `configs/` ที่ตั้ง `send_to_backend: true` และ `backend_url` ถูกต้อง
- [ ] ต้องใช้ `python -m src.main` (รัน YOLO จริง) — **ไม่ใช่** `src.replay` ซึ่งไม่มีขั้น AI

**Backend**
- [ ] `backend/.env` มีบรรทัด `LATENCY_LOG=true`

**Unity**
- [ ] เปิดซีน `Traffic` ใน Editor (WebGL build เขียนไฟล์ไม่ได้ — จะ log ลง browser console แทน)
- [ ] ติ๊ก **Log Latency** ที่ GameObject ที่มี `SpawnVehicleTestClient`
- [ ] **Project Settings → Player → Resolution and Presentation → Run In Background = ✅**
      (ถ้าปิด Unity จะหยุดรับ event เมื่อหน้าต่างไม่ได้ focus — ดู §8)

---

## 6. ขั้นตอนการทดลอง

> **ต้องรีสตาร์ตครบทั้ง 3 ตัวทุกรอบ** — แต่ละตัวล้างไฟล์ log ตอนเริ่มทำงาน

1. **Backend** — terminal ที่ 1
   ```bash
   cd backend
   npm run dev
   ```
   ต้องเห็น `[latency] Logging timestamps to ...latency_backend.csv`

2. **Unity** — กด **Play** ในซีน `Traffic`
   Console ต้องขึ้น `[LatencyLog] Logging timestamps to ...latency_unity.csv`
   และ `Connected to http://localhost:3000`

3. **AI Worker** — terminal ที่ 2
   ```bash
   cd ai-worker
   python -m src.main --config configs/<ไฟล์>.yaml --latency-log
   ```
   ต้องเห็น `[latency] Logging timestamps to ...latency_ai.csv`
   (ใช้ `--latency-log` แทนการตั้ง env — คำสั่งตั้ง env ต่างกันระหว่าง Git Bash / PowerShell)

4. **รอให้ AI Worker รันจบคลิป** แล้วค่อยกด Stop ใน Unity และหยุด backend

5. **วิเคราะห์ผล**
   ```bash
   cd ai-worker
   python -m src.analyze_latency
   ```

6. **ตรวจความถูกต้องของรอบนี้** (§7) ก่อนนำผลไปใช้

7. **บันทึกผล** ลงตาราง `tab:latency` ใน `docs/Chapter4.tex`, Metrics Log ใน
   `docs/project-status.md` และเก็บ `latency_merged.csv` ไว้แนบภาคผนวก
   (ก็อปออกไปเก็บ เพราะรันรอบถัดไปจะถูกเขียนทับ)

### ตัวอย่างผลลัพธ์

```
[latency] 47 complete events

                                    n   Median (ms)    P95 (ms)
Segment 1 - AI Processing          47         171.8       180.6
Segment 2 - HTTP POST Ingest       47           2.8        18.1
Segment 3 - WS Broadcast           47           0.0         1.0
Segment 4 - Unity Receive          47           6.0        ...
E2E Total                          47         183.7        ...
  (in seg 1) YOLO+tracking         47         171.4       180.5
  Spawn queue wait (extra)         47           2.0      1358.3

E2E Median <= 1000 ms : PASS (183.7 ms)
```

| exit code | ความหมาย |
|---|---|
| 0 | ผ่านเกณฑ์ |
| 1 | หาไฟล์ log ไม่เจอ / ไม่มี event ที่ครบทุก stage |
| 2 | ไม่ผ่านเกณฑ์ (E2E Median > 1000 ms) |
| 3 | มีเวลาย้อนกลับ — ผลทั้งชุดเชื่อไม่ได้ (join ผิดหรือนาฬิกาไม่ตรง) |

---

## 7. เช็คลิสต์ก่อนนำผลไปใช้

ผลรอบหนึ่ง **ใช้ได้** เมื่อ:
- [ ] ไม่มีข้อความ `[warn] ... repeated rows` (ถ้ามี = มีบาง module ไม่ได้รีสตาร์ต)
- [ ] `complete events` ≥ 30
- [ ] ไม่มีบรรทัด `incomplete dropped` หรือมีน้อยและอธิบายได้
      (`stop before unityReceived` เป็นกลุ่มท้ายคลิป = หยุด Unity ก่อน AI รันจบ)
- [ ] P95 ของ Segment 4 ไม่กระโดดเป็นหลักวินาที (ถ้ากระโดด ดู §8 ข้อ 2)
- [ ] exit code เป็น 0 หรือ 2 (ไม่ใช่ 3)

---

## 8. ปัญหาที่เจอจากการรันจริง (2026-11-06)

1. **ไม่ได้รีสตาร์ต backend → trackId ซ้ำข้ามรอบ**
   รันคลิปเดิมซ้ำได้ trackId ชุดเดิม เวอร์ชันแรกของสคริปต์ join ด้วย "แถวแรก" จึงจับเวลา
   ของรอบเก่ามาลบกับรอบใหม่ ได้ Segment 2 = **−699,203 ms** แต่ E2E ดันขึ้น PASS
   **แก้แล้ว:** จับคู่ตามลำดับเวลา (แต่ละ stage เลือกค่าแรกที่ไม่เกิดก่อน stage ก่อนหน้า)
   และถ้ายังมี segment ติดลบ สคริปต์จะ exit 3 ไม่รายงานผล

2. **Unity ไม่ทำงานเมื่อหน้าต่างไม่ได้ focus** (`Run In Background = false`)
   5 คันที่ส่งออกช่วง 20 วินาทีไปถึง Unity **พร้อมกันในวินาทีเดียว** (Segment 4 สูงถึง
   22.9 วินาที) และ 24 คันท้ายคลิปไม่ถึง Unity เลยหลังสลับไปพิมพ์คำสั่งใน terminal
   ปกติ Segment 4 อยู่ที่ 2–8 ms → ค่าผิดปกตินี้มาจากตัว Editor ไม่ใช่ระบบ
   **แก้:** เปิด Run In Background (§5)

3. **ตั้ง env ผิด shell** — ใช้ `$env:LATENCY_LOG="true"` (PowerShell) ใน Git Bash ทำให้
   AI ไม่ได้ log **แก้แล้ว:** เพิ่ม flag `--latency-log`

---

## 9. ตัวเลขเดิมใน Chapter4.tex ไม่ตรงกับผลวัดจริง (แก้แล้ว)

ตาราง `tab:latency` ใน `docs/Chapter4.tex` (และ `docs/Draft Chapter 4.md`) มีตัวเลขอยู่แล้ว
**ก่อนที่เครื่องมือวัดชุดนี้จะถูกสร้าง** และไม่สอดคล้องกับการวัดจริง:

| | Chapter4.tex | วัดจริง (รอบทดลอง 2026-11-06) |
|---|---|---|
| Segment 1 — AI Processing (Median) | 22.4 ms | **171.8 ms** (YOLO อย่างเดียว 171.4 ms) |
| E2E (Median) | 47.8 ms | **183.7 ms** |

อีกข้อสังเกต: ค่า E2E ในตารางเท่ากับ **ผลบวกของ median แต่ละ segment** พอดี
(22.4 + 4.8 + 2.1 + 18.5 = 47.8) และ P95 ก็เป็นผลบวกของ P95 พอดี (38.1 + 11.2 + 5.5 + 45.0
= 99.8) — ทางสถิติ median/P95 ของผลรวม **ไม่เท่ากับ** ผลรวมของ median/P95 ถ้าวัดจากข้อมูล
จริงจะไม่ออกมาลงตัวแบบนี้

**✅ แก้แล้ว (2026-11-07):** แทนด้วยผลรอบที่ผ่านเช็คลิสต์ §7 (n=72, Run In Background เปิด,
รีสตาร์ตครบ 3 module) — E2E Median 181.8 ms · P95 211.1 ms ทั้งใน Chapter4.tex
(ตาราง tab:latency + ย่อหน้าวิเคราะห์ + ตาราง Acceptance) และ Draft Chapter 4.md
แถว 4 เปลี่ยนชื่อเป็น "Unity WebSocket Receive" ให้ตรงกับสิ่งที่วัด (§3.3)
