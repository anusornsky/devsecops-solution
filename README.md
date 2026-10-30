# DevSecOps Lab — stock API (Go + C#)

![security](https://github.com/anusornsky/devsecops-lab/actions/workflows/security.yml/badge.svg) ![ci](https://github.com/anusornsky/devsecops-lab/actions/workflows/ci.yml/badge.svg) ![deploy](https://github.com/anusornsky/devsecops-lab/actions/workflows/deploy.yml/badge.svg)

## ทำอะไร

pipeline ที่ตรวจ security ทุกครั้งที่ push แล้ว deploy ขึ้น AWS (ECR → ECS Fargate) โดยไม่มี AWS key เก็บไว้ที่ไหนเลย (OIDC)

## ด่านตรวจ

| ด่าน | เครื่องมือ | จับอะไร | จับอะไรไม่ได้ (ลองแล้ว) | หลักฐาน |
|---|---|---|---|---|
| secret | gitleaks (ทุก commit) | key ที่ลบไปแล้วแต่ยังอยู่ใน history | key ที่ไม่ตรงรูปแบบ `AKIA[A-Z2-7]{16}` · ถ้า checkout ไม่มี `fetch-depth: 0` เห็นแค่ commit ล่าสุด | job `secrets` |
| SAST | Semgrep `p/csharp` `p/golang` | pattern อันตรายทั่วไป | `FromSqlRaw` ที่ต่อ string ใน Step 3 (0 findings) | job `sast` |
| test | xUnit / go test | SQL injection ใน `/products/search` | — | commit `test(step3)` แดง → `fix(step3)` เขียว |
| SCA | govulncheck · NuGet audit | Go stdlib 1.26.7 (8 ข้อ) · SQLite native lib 2.1.10 (High) | ช่องโหว่ที่ยังไม่ประกาศ | job `sca` (รายงาน — เปิด log) |
| image | SBOM (Syft) · Grype | 410 CVE ใน image แบบ unsafe vs 7 ใน chiseled | — | [docs/05-image-compare.md](docs/05-image-compare.md) |
| DAST | ZAP baseline | header ที่ขาด (`nosniff` แก้แล้ว) | SQL injection / IDOR (baseline ไม่ยิง payload) | job `dast` (artifact zap-report) |
| IaC | Checkov | bucket public · ไม่เข้ารหัส · ECR tag เปลี่ยนได้ | ค่าใช้จ่าย · เปลี่ยนชื่อ resource แล้ว destroy ของจริง (ต้องอ่าน plan เอง) | job `checkov` · skip ทุกข้อมีเหตุผลในโค้ด |
| deploy | OIDC → ECR → ECS | — | — | [docs/07-oidc.md](docs/07-oidc.md) |

## เรื่องที่เจอจริง

- ใส่ช่องโหว่ SQL injection เอง (tag `vuln-demo`) → เจาะ → test แดง → แก้ → เขียว · [post-mortem](docs/postmortem-001.md)
- image unsafe vs chiseled: CVE 410 vs 7 · ขนาด 1.56GB vs 231MB
- Go 1.26.7 มีช่องโหว่ stdlib 8 ข้อ / SQLite native lib High → อัปเกรดแล้วสะอาด test ผ่านหมด
- `sdk:latest` เปลี่ยนจาก .NET 9 เป็น 10 ทำ build พัง (global.json ล็อก 9.0.317) → เหตุผลที่ต้องล็อกเวอร์ชัน

## รันบน AWS

ECS Fargate 1 task (public IP · ไม่มี load balancer · ~$9/เดือน) · หา URL: `& .\infra\url.ps1` · ปิด: `terraform -chdir=infra apply -var desired_count=0`

## ถ้าทำใหม่จะทำอะไรต่าง

- เพิ่ม request log + alert ตั้งแต่แรก — ตอนนี้รู้ตัวช้าเพราะ log แค่ error
- ใช้ Postgres/RDS แทน SQLite ใน container ถ้าข้อมูลต้องอยู่รอดข้าม deploy

---

# go-csharp-lab — API สต็อกสินค้า เขียน 2 ภาษาวางเทียบกัน

ระบบเดียวกันทุกอย่าง (URL · กฎ · ข้อความ error · ชื่อตาราง · ชื่อเทสต์) เขียนด้วย **Go** และ **C# (ASP.NET Core + EF Core)**
ไม่ได้เรียกหากัน — มีไว้เปิดไฟล์คู่กันแล้วดูว่า "เรื่องเดียวกัน 2 ภาษาทำยังไง"

> มือใหม่: อ่าน [docs/reading-guide.md](docs/reading-guide.md) ก่อน — ลำดับการอ่าน + พื้นฐานที่ต้องรู้ทีละไฟล์

## ระบบทำอะไร

คลังสินค้า: สร้างสินค้า · รับของเข้า · เบิกของออก (เบิกเกินที่มีไม่ได้ แม้มีคนกดพร้อมกัน 20 คน)

| คำขอ | ผล |
|------|----|
| `GET /healthz` | 200 `{"status":"ok"}` · ฐานข้อมูลล่ม = 503 |
| `POST /products` `{"sku","name","qty"}` | 201 · SKU ซ้ำ 409 · ผิดกฎ 400 |
| `GET /products` | 200 `[...]` (ว่าง = `[]`) |
| `GET /products/{sku}` | 200 · ไม่มี 404 |
| `POST /products/{sku}/receive` `{"qty"}` | 200 ยอดใหม่ |
| `POST /products/{sku}/issue` `{"qty"}` | 200 ยอดใหม่ · ของไม่พอ 409 |

กฎ: SKU `A-Z 0-9 -` ยาว 3-20 ตัว (ตัดช่องว่าง + แปลงเป็นตัวใหญ่ให้) · ต้องมีชื่อ · จำนวนเริ่มต้น ≥ 0 · ราคา ≥ 0 (หน่วยสตางค์ `1500` = 15 บาท) · รับ/เบิกต้อง > 0

### ระบบสั่งซื้อ + งานเบื้องหลัง (worker)

| คำขอ | ผล |
|------|----|
| `POST /orders` `{"items":[{"sku","qty"}]}` | **202** `{"id":1,"status":"pending",...}` — รับเรื่องแล้ว ยังไม่ได้ตัดสต็อก · ผิดกฎ 400 |
| `GET /orders/{id}` | 200 `{"status":"done","total":8000,...}` หรือ `"failed"` + `reason` · ไม่มี 404 |

```
POST /orders ──► orders (pending) ──► worker หยิบทุก 0.5 วินาที
                                        │  transaction เดียว:
                                        │   ตัดสต็อกทุกรายการ + จำราคา + done
                                        ├─ สำเร็จ                 → done + total
                                        ├─ ไม่มีสินค้า/ของไม่พอ/ยอดรวมล้น → failed ทันที (rollback ไม่มีรายการไหนถูกตัด)
                                        └─ พังชั่วคราว            → pending อีกครั้ง รอ 2 วิ / 4 วิ · ครบ 3 ครั้ง → failed
เปิดโปรแกรม + รอบถัดจากรอบที่ฐานข้อมูลพัง: order ที่ค้าง processing → คืนเข้าคิว
ปิดโปรแกรม: worker ทำงานชิ้นที่ทำอยู่ให้จบก่อน แล้วค่อยปิดฐานข้อมูล
```

> ⚠️ มีไฟล์ `stock.db` จากก่อนเพิ่มราคา/order → **ลบทิ้งก่อนรัน** (ไม่มี migration — `CREATE TABLE IF NOT EXISTS` / `EnsureCreated` ไม่แก้ตารางที่มีอยู่)

ข้อแลกที่ยอมรับ: ตอนปิด ทั้งสองภาษาหยุด worker ก่อน HTTP · order ที่เข้ามาในช่วงนั้น (ไม่ถึงวินาที) ถูกเก็บเป็น pending ไม่หาย ทำต่อตอนเปิดใหม่

## รันบนเครื่อง

ต้องมี: Go 1.26+ · .NET SDK 9

```powershell
# Go  → http://127.0.0.1:8082
cd go ; go run ./cmd/stockapi

# C#  → http://127.0.0.1:8081   (หรือเปิด csharp\StockApi.sln ใน VS 2022 แล้วกด F5)
cd csharp ; dotnet run --project StockApi
```

แล้วเปิด [`demo/stock.http`](demo/stock.http) ใน VS 2022 → กด **Send request** ทีละบล็อก (เปลี่ยน `@host` เพื่อสลับภาษา)

ตั้งค่าผ่าน environment variable:

| | Go | C# |
|---|---|---|
| ไฟล์ฐานข้อมูล | `DB_PATH` (default `stock.db`) | `DB_PATH` (default `stock.db`) |
| port | `ADDR` (default `127.0.0.1:8082`) | `ASPNETCORE_URLS` (ระบบของ ASP.NET เอง) |

## รันแบบ Docker (กล่องเดียวกับที่จะขึ้น AWS)

**รันบนเครื่อง** = ใช้ Go/.NET ที่ลงไว้ในเครื่อง · เร็ว แก้แล้วรันได้ทันที → ใช้ตอนเขียนโค้ด
**Docker** = มัดโปรแกรม + ของที่ต้องใช้ไว้ในกล่องเดียว รันที่ไหนก็เหมือนกัน · server ไม่ต้องลง Go/.NET → ใช้ตอนส่งขึ้น server

```powershell
docker build -t stockapi-go go
docker build -t stockapi-cs csharp
docker run --rm -p 127.0.0.1:8090:8080 -v stockgo:/data stockapi-go   # → http://127.0.0.1:8090
docker run --rm -p 127.0.0.1:8091:8080 -v stockcs:/data stockapi-cs   # → http://127.0.0.1:8091
```

`-v stockgo:/data` = เก็บไฟล์ฐานข้อมูลไว้นอกกล่อง ลบ container ทิ้งข้อมูลยังอยู่ (ถ้าไม่ใส่ ลบกล่องเมื่อไหร่ข้อมูลหายหมด)

| | Go | C# |
|---|---|---|
| กล่องฐาน | `distroless/static-debian12:nonroot` | `aspnet:9.0-noble-chiseled` |
| ขนาด image (วัดจริง) | **21 MB** | **224 MB** (ต้องมี .NET runtime ในกล่อง) |
| มี shell ไหม | ไม่มี (`docker exec ... sh` → not found) | ไม่มี |
| รันเป็น | user `nonroot` | user `1654` (app) |
| `docker stop` | ปิดนุ่มนวล 0.5 วินาที | ปิดนุ่มนวล 0.5 วินาที |

## CI (GitHub Actions)

ทุก push เข้า `main` / ทุก PR → [`.github/workflows/ci.yml`](.github/workflows/ci.yml) รัน 2 เครื่องพร้อมกัน:

```
[go]      gofmt → go vet → go test -race -cover → docker build
[dotnet]  restore → dotnet format → build → test + coverage → docker build
```

action ที่ใช้มีแค่ของ GitHub (`checkout` · `setup-go` · `setup-dotnet`) ล็อกด้วย SHA

## รันเทสต์

```powershell
cd go ; go test -cover ./...
cd csharp ; dotnet test --collect:"XPlat Code Coverage"
```

| | Go | C# |
|---|---|---|
| จำนวน | 5 package ผ่านหมด | 27 ผ่าน |
| coverage (วัด 2026-10-14) | domain 100% · httpapi 87.5% · app 80% · main 67.7% · store 63.6% | line 94% · branch 77% |

Go นับ `if err != nil { return err }` ทุกจุดเป็นบรรทัดแยก จึงดูต่ำกว่าใน `store` — ไม่ได้แปลว่าเทสต์น้อยกว่า

## เทียบไฟล์ต่อไฟล์

| ชั้น | หน้าที่ | Go | C# |
|------|--------|----|----|
| Domain | กฎของงานล้วนๆ | `go/internal/domain/product.go` | `csharp/StockApi/Domain/Product.cs` + `StockExceptions.cs` |
| Application | ลำดับงาน + "สัญญา" ของที่เก็บข้อมูล | `go/internal/app/service.go` | `Application/StockService.cs` + `IProductRepository.cs` |
| Infrastructure | คุยกับฐานข้อมูล | `go/internal/store/sqlite.go` (SQL เขียนเอง) | `Infrastructure/EfProductRepository.cs` + `StockDbContext.cs` (EF Core) |
| Api | HTTP ↔ service | `go/internal/httpapi/handler.go` | `Api/ProductEndpoints.cs` + `ErrorHandling.cs` |
| ต่อทุกชั้น | config · log · server | `go/cmd/stockapi/main.go` | `Program.cs` |
| **Order** Domain | กฎของคำสั่งซื้อ + สถานะ | `go/internal/domain/order.go` | `Domain/Order.cs` |
| **Order** Application | รับ order + ตัดสินใจ retry/failed | `go/internal/app/orders.go` | `Application/OrderService.cs` + `IOrderRepository.cs` |
| **Order** Infrastructure | จองงาน · transaction ตัดสต็อก | `go/internal/store/orders.go` | `Infrastructure/EfOrderRepository.cs` |
| **Order** Worker | วนทำงานเบื้องหลัง | `go/internal/worker/worker.go` (goroutine) | `Worker/OrderWorker.cs` (`BackgroundService`) |

### ต่างกันตรงไหนบ้าง

| เรื่อง | Go | C# |
|--------|----|----|
| error | คืนเป็นค่า `(Product, error)` แล้วเช็ค `errors.Is` | `throw` exception แล้วจับตามชนิด |
| แปลง error → status | ฟังก์ชัน `respond` | middleware `UseExceptionHandler` |
| ส่งของให้กัน (DI) | ส่งเองใน `main.go` | DI container ใน `Program.cs` |
| ฐานข้อมูล | SQL ตรงๆ ผ่าน `database/sql` | LINQ → EF Core แปลงเป็น SQL |
| เบิกแบบ atomic | `UPDATE … WHERE sku=? AND qty>=?` | `.Where(p => … && p.Qty >= qty).ExecuteUpdateAsync(…)` → **ได้ SQL เดียวกัน** |
| ทำงานพร้อมกัน | goroutine | `async`/`await` + `Task` |
| ปิดอย่างนุ่มนวล | เขียนเอง (`signal.NotifyContext` + `Shutdown`) | มีในตัว (`HostOptions.ShutdownTimeout`) |

## ผลรันจริง (2026-10-14 · ยิงชุดเดียวกันใส่ทั้ง 2 ภาษา)

```
GET  /healthz                         → 200 {"status":"ok"}
POST /products {" ab-001 ",น็อต M6,10} → 201 {"sku":"AB-001","name":"น็อต M6","qty":10}
POST /products (ซ้ำ)                  → 409 {"error":"มีสินค้านี้แล้ว"}
POST /products {"x","",-1}            → 400 {"error":"ข้อมูลไม่ถูกต้อง: sku \"X\" ต้องเป็น A-Z 0-9 - ยาว 3-20 ตัว"}
GET  /products/NOPE-1                 → 404 {"error":"ไม่พบสินค้า"}
POST /products/AB-001/receive {5}     → 200 qty 15
POST /products/AB-001/issue {20}      → 409 {"error":"สต็อกไม่พอ"}
POST /products/AB-001/issue {15}      → 200 qty 0
POST /products {"nmae":...}           → 400 JSON ไม่ถูกต้อง
DELETE /products/AB-001               → 405
```

status และข้อความไทยตรงกันทุกบรรทัด · ต่างแค่ข้อความ error ของตัวอ่าน JSON (มาจาก library ของแต่ละภาษา)

SQL ที่ EF Core สร้างจริงตอนเบิก (เปิดดูได้ด้วย `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information`):

```sql
UPDATE "products" AS "p" SET "qty" = "p"."qty" - @__qty_1
WHERE "p"."sku" = @__sku_0 AND "p"."qty" >= @__qty_1
```

### พิสูจน์ว่าเทสต์ "เบิกพร้อมกัน" จับของพังได้จริง

แก้โค้ดเบิกชั่วคราวให้เป็นแบบ "อ่านยอด → เช็ค → เขียนทับ" แล้วรันเทสต์ `ConcurrentIssueNeverOversells`:

```
Expected: 10
Actual:   20      ← เบิกสำเร็จ 20 ครั้งจากของ 10 ชิ้น = ขายเกิน
```

คืนโค้ดแบบ atomic → ผ่าน 27/27

## กับดักที่เจอระหว่างทำ

- **curl บน Windows ส่งภาษาไทยเพี้ยน** ถ้าใส่ JSON ในบรรทัดคำสั่งตรงๆ (ส่งเป็น code page ไทยแทน UTF-8) → ใช้ `--data-binary @file.json` หรือไฟล์ `.http`
- **port 8080 ชน** — Docker Desktop จองไว้ · Go เลยใช้ 8082
- **C# log ไทยเพี้ยนตอนส่ง stdout ลงไฟล์บน Windows** → `Console.OutputEncoding = UTF8`
- **C# log 404/409 เป็น Error** (ทั้ง ASP.NET และ EF) ทั้งที่เป็นเรื่องปกติของงาน → ปิดใน `Program.cs` ให้ log เฉพาะ 500 เหมือน Go
