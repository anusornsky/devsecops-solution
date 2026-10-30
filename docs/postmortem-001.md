# Post-mortem 001 — SQL injection ใน /products/search

> จำลองใน lab (Step 3 + Step 9) · ไม่โทษคน โทษระบบ

## สรุป

endpoint ค้นหาสินค้าต่อข้อความของผู้ใช้เข้า SQL ตรงๆ ทำให้คนนอกอ่านข้อมูลทุกแถวและโครงสร้างฐานข้อมูลได้
ตั้งแต่ commit `feat(step3): product search endpoint` (tag `vuln-demo`) จนถึง `fix(step3): parameterize search query`

## timeline (จาก access log ที่จำลองใน Step 9 — status/จำนวนแถวตรงกับแอปจริง)

| เวลา | เหตุการณ์ |
|---|---|
| 10:02:40 | ส่ง `'` ตัวเดียว → 500 = **เริ่มลองเจาะ** |
| 10:02:41 | ส่ง `''` → 200 = ยืนยันว่า input ไปถึง SQL |
| 10:03:05 | `' OR 1=1 --` → ได้ทุกแถว = **เจาะสำเร็จ** |
| 10:03:30 | `UNION SELECT name,sql,0,0 FROM sqlite_master WHERE sql IS NOT NULL` → 7 แถว = **ได้โครงสร้างทุกตาราง** |

## รู้ตัวได้ยังไง / ทำไมช้า

- แอปบันทึกเฉพาะ error (`StockApi.Errors`) ไม่มี request log → เห็นแค่ 500 ตอน 10:02:40 แต่ไม่มี alert
- ถ้ามี alert "error จาก endpoint เดียวกัน ≥ 1 ใน 1 นาที" จะเตือนก่อนเจาะสำเร็จ **25 วินาที**

## ด่านไหนควรจับได้

| ด่าน | จับได้ไหม | หมายเหตุ |
|---|---|---|
| Semgrep (SAST) | ❌ | `p/csharp` ได้ 0 findings แม้บนโค้ดที่มีช่องโหว่ |
| ZAP baseline (DAST) | ❌ | baseline ไม่ยิง payload โจมตี |
| test ของเราเอง | ✅ | `Search_ignores_sql_injection` แดงทันที — เลยต้องมี test |

## แก้แล้ว

- ใช้ parameter (`FromSql($"...{q}...")` / Go `?`) — PR แดง (มีแค่ test) → เขียว (มีตัวแก้)
- test ค้างไว้ใน CI กันช่องโหว่กลับมา

## จะทำเพิ่ม

- [ ] เพิ่ม request log (path · status · ผู้ใช้) และ alert error พุ่ง (A09)
- [ ] กฎ Semgrep ของเราเอง จับ `FromSqlRaw(` ที่มีการต่อ string
