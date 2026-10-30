# คู่มืออ่านโค้ด — ไล่ตาม 1 คำขอ จากต้นจนจบ

วิธีที่เร็วที่สุดในการเข้าใจระบบ: **เลือก 1 คำขอ แล้วตามมันไปทุกไฟล์** ไม่ต้องอ่านทุกไฟล์จากบนลงล่าง
คู่มือนี้ตาม `POST /products/AB-001/issue {"qty":3}` (เบิกของ 3 ชิ้น) ผ่านทั้ง 2 ภาษา

ทุกไฟล์มี comment `── พื้นฐาน ──` อธิบาย syntax ที่เจอครั้งแรกไว้แล้ว — คู่มือนี้บอกแค่ "อ่านไฟล์ไหน เพื่ออะไร"

---

## ภาพรวม: ทำไมแบ่งเป็น 4 ชั้น

```
 คำขอ HTTP
    │
    ▼
┌─────────┐   แปลง JSON ↔ ค่าในโปรแกรม · แปลง error ↔ HTTP status
│   Api   │   (รู้จัก HTTP · ไม่รู้กฎธุรกิจ)
└────┬────┘
     ▼
┌─────────────┐  ลำดับงาน: ตรวจกฎ → สั่งเก็บ → เขียน log
│ Application │  (ไม่รู้ว่าข้างล่างเป็น SQLite หรืออะไร — รู้แค่ "สัญญา" Repository)
└────┬────────┘
     ▼                         ┌─────────┐
┌────────────────┐  ใช้กฎจาก → │ Domain  │ กฎล้วนๆ: SKU ต้องเป็นแบบไหน, จำนวนต้อง > 0
│ Infrastructure │             └─────────┘ (ไม่รู้จัก HTTP · SQL · ไฟล์ — เทสต์ง่ายสุด)
└────┬───────────┘
     ▼
  SQLite (ไฟล์ stock.db)
```

**ทำไมต้องแบ่ง?** เปลี่ยนชั้นหนึ่งแล้วชั้นอื่นไม่ต้องแก้ — เช่นวันหนึ่งย้ายจาก SQLite ไป PostgreSQL ก็เขียน Infrastructure ใหม่ชั้นเดียว
และเทสต์แต่ละชั้นแยกกันได้ (เทสต์ service ด้วยที่เก็บปลอม ไม่ต้องมีฐานข้อมูล)

**ทางที่ไม่เลือก:** เขียนทุกอย่างใน endpoint เดียว (อ่าน JSON → เช็คกฎ → SQL → ตอบ) — สั้นกว่าสำหรับระบบเล็กนี้
แต่งานจริงโตขึ้นเรื่อยๆ และนี่คือโครงที่จะเจอในบริษัท จึงเลือกแบบแบ่งชั้นเพื่อฝึก

---

## ตามคำขอ "เบิก 3 ชิ้น" ทีละขั้น

### ขั้น 0 — โปรแกรมเริ่มทำงาน (อ่านก่อน)

| Go | C# |
|----|----|
| `go/cmd/stockapi/main.go` | `csharp/StockApi/Program.cs` |

อ่านหา 4 อย่าง:
1. **อ่าน config** ที่ไหน (`DB_PATH`)
2. **ต่อชิ้นส่วน** ยังไง — Go: `app.NewService(db, log)` ส่งเองตรงๆ · C#: `AddScoped<...>()` บอก DI container ให้สร้างให้
3. **log** ออกรูปแบบไหน (JSON ทั้งคู่)
4. **ปิดยังไง** เมื่อกด Ctrl+C

> **พื้นฐาน — Dependency Injection (DI):** คลาส/ฟังก์ชัน "ไม่สร้างของที่ตัวเองใช้เอง" แต่ "รับมาจากข้างนอก"
> เช่น `StockService` ไม่ `new EfProductRepository()` เอง แต่รับ `IProductRepository` มาทาง constructor
> ผลคือเทสต์ส่ง `MemRepo` (ของปลอม) เข้าไปแทนได้ — ดู `StockServiceTests.cs`

### ขั้น 1 — คำขอเข้ามาที่ Api

| Go | C# |
|----|----|
| `go/internal/httpapi/handler.go` → `mux.HandleFunc("POST /products/{sku}/issue", ...)` | `Api/ProductEndpoints.cs` → `products.MapPost("/{sku}/issue", ...)` |

สิ่งที่เกิด: ดึง `AB-001` จาก URL · อ่าน `{"qty":3}` จาก body · เรียก service
- Go ทำเองทุกขั้น: `r.PathValue("sku")` + `decode(w, r, &req)`
- C# ระบบเติมให้: แค่ประกาศ parameter `string sku, MoveRequest req` ASP.NET หาค่ามาใส่เอง

### ขั้น 2 — Application ตรวจและสั่งงาน

| Go | C# |
|----|----|
| `go/internal/app/service.go` → `func (s *Service) Issue` | `Application/StockService.cs` → `IssueAsync` |

1. `ValidateMove(3)` — 3 > 0 ผ่าน (ถ้าส่ง 0 จะหยุดตรงนี้ → 400)
2. เรียก `repo.Issue(...)` — **ไม่รู้ว่าเป็น SQLite** รู้แค่ว่ามี method ชื่อนี้ตาม "สัญญา"
3. สำเร็จ → log `เบิกออก` · ไม่สำเร็จ → log `เบิกไม่สำเร็จ` เป็น Warning

> **พื้นฐาน — interface (สัญญา):** บอกว่า "ต้องมี method อะไร" ไม่มีโค้ดข้างใน
> Go: `type Repository interface {...}` อยู่ใน `service.go` (ประกาศฝั่งคนใช้)
> C#: `IProductRepository.cs`

> **พื้นฐาน — async/await (C#) vs goroutine (Go):** ระหว่างรอฐานข้อมูลตอบ ไม่ปล่อย thread ว่างเปล่า
> C# เขียน `await` ทุกจุดที่รอ · Go ไม่ต้องเขียนอะไร — runtime จัดการให้ (แต่ละคำขอได้ goroutine ของตัวเองอยู่แล้ว)

### ขั้น 3 — Infrastructure คุยกับฐานข้อมูล ⭐ หัวใจของระบบ

| Go | C# |
|----|----|
| `go/internal/store/sqlite.go` → `func (s *SQLite) Issue` | `Infrastructure/EfProductRepository.cs` → `IssueAsync` |

ทั้งคู่ส่ง SQL **คำสั่งเดียว**:
```sql
UPDATE products SET qty = qty - 3 WHERE sku = 'AB-001' AND qty >= 3
```
- แก้ได้ 1 แถว → สำเร็จ อ่านยอดใหม่ส่งกลับ
- แก้ได้ 0 แถว → ไม่มีสินค้า **หรือ** ของไม่พอ → อ่านซ้ำเพื่อแยกว่ากรณีไหน

> **พื้นฐาน — race condition (ทำไมต้องคำสั่งเดียว):**
> ถ้าเขียนแบบ "อ่านยอด → เช็คในโค้ด → เขียนยอดใหม่" แล้วมี 2 คนกดพร้อมกัน:
> ```
> คน A อ่านได้ 10 ─┐
> คน B อ่านได้ 10 ─┤ ทั้งคู่เห็นว่าพอ
> คน A เขียน 10-10=0
> คน B เขียน 10-10=0   ← ของ 10 ชิ้น ถูกเบิกไป 20
> ```
> ใส่เงื่อนไขไว้ใน `WHERE` → ฐานข้อมูลทำทีละคำสั่ง คนที่สองแก้ได้ 0 แถว
> เทสต์ `ConcurrentIssueNeverOversells` (ทั้ง 2 ภาษา) พิสูจน์เรื่องนี้ — และเคยลองแก้เป็นแบบผิดแล้ว เทสต์จับได้จริง (ดู README)

> **พื้นฐาน — EF Core:** เขียน LINQ (`.Where(p => ...)`) แทน SQL · EF แปลงเป็น SQL ให้
> `ExecuteUpdateAsync` = ส่ง UPDATE ตรงๆ ไม่โหลด object ขึ้นมาก่อน (ถ้าโหลดก่อน = กลับไปเป็นแบบ race ข้างบน)

### ขั้น 4 — ผลกลับขึ้นไป แปลงเป็น HTTP

| ผล | Go (`respond` ใน handler.go) | C# (`Api/ErrorHandling.cs`) |
|----|----|----|
| สำเร็จ | `writeJSON(200, p)` | `Results.Ok(p)` |
| ของไม่พอ | `errors.Is(err, ErrInsufficientStock)` → 409 | `InsufficientStockException` → 409 |
| อื่นๆ ที่ไม่รู้จัก | 500 + log รายละเอียด (ไม่ส่งให้ client) | เหมือนกัน |

> **พื้นฐาน — error เป็นค่า (Go) vs exception (C#):**
> Go: ทุกฟังก์ชันที่พังได้คืน `(ผลลัพธ์, error)` → คนเรียกต้อง `if err != nil` ทุกครั้ง · เห็นทุกจุดที่พังได้ชัด แต่ยาว
> C#: `throw` แล้วข้ามไปถึงคนที่ `catch` เลย · โค้ดสั้น แต่มองไม่เห็นว่าบรรทัดไหน throw ได้บ้าง

---

## อ่านเทสต์ — เข้าใจพฤติกรรมเร็วที่สุด

เทสต์ = ตัวอย่างการใช้งานที่รันได้ · ชื่อเทสต์ตรงกัน 2 ภาษา เปิดคู่กันได้เลย

| ชั้น | Go | C# | พิสูจน์อะไร |
|------|----|----|----|
| Domain | `domain/product_test.go` | `Domain/ProductTests.cs` | กฎ SKU / ชื่อ / จำนวน ครบทุกข้อ |
| Application | `app/service_test.go` | `Application/StockServiceTests.cs` | ลำดับงานถูก ใช้ที่เก็บปลอม (ไม่มี DB) |
| Infrastructure | `store/sqlite_test.go` | `Infrastructure/EfProductRepositoryTests.cs` | SQL ทำงานจริง + เบิกพร้อมกัน 20 งาน |
| Api | `httpapi/handler_test.go` | `Api/ApiTests.cs` | ยิง HTTP จริงทุกชั้น ได้ status ถูก |
| main | `cmd/stockapi/main_test.go` | — | เปิด server จริง แล้วปิดสะอาด |

ทุกเทสต์เขียนแบบ **AAA**: Arrange (เตรียมของ) → Act (เรียกสิ่งที่ทดสอบ) → Assert (เช็คผล)

---

## ลำดับแนะนำ (ประมาณ 2-3 ชั่วโมง)

1. รันทั้ง 2 ตัว + ยิง `demo/stock.http` ให้เห็นผลก่อน (15 นาที)
2. `Domain` ทั้ง 2 ภาษา + เทสต์ของมัน — สั้นที่สุด พื้นฐานภาษาเยอะสุด
3. `Application` + เทสต์ (ดูว่าของปลอม `memRepo` / `MemRepo` ทำงานยังไง)
4. `Infrastructure` — ตั้งใจอ่าน `Issue` และเทสต์เบิกพร้อมกัน
5. `Api` → `main.go` / `Program.cs` ปิดท้าย

**ลองแก้เองเพื่อเช็คความเข้าใจ:** เพิ่มกฎ "ชื่อสินค้ายาวไม่เกิน 100 ตัว" ทั้ง 2 ภาษา
→ แก้ Domain → เพิ่มบรรทัดในเทสต์ตาราง → `go test ./...` / `dotnet test` ต้องผ่าน
