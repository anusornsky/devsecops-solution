// adapter ขาเข้า: ผูก URL → เรียก StockService → ส่ง JSON กลับ
// เทียบ Go: go/internal/httpapi/handler.go (ส่วน mux.HandleFunc)
using Microsoft.EntityFrameworkCore;
using StockApi.Application;
using StockApi.Infrastructure;

namespace StockApi.Api;

// รูปร่าง JSON ที่รับเข้า (DTO = Data Transfer Object) แยกจาก Product
// เหตุผล: คนนอกส่งอะไรมาก็ได้ ต้องผ่าน Product.Create ก่อนถึงจะเป็นสินค้า · Go: struct createReq / moveReq
public sealed record CreateProductRequest(string? Sku, string? Name, int Qty, long Price); // Price = สตางค์ · ไม่ส่งมา = 0
public sealed record MoveRequest(int Qty);

// static class + this WebApplication app = "extension method" → เรียกใน Program.cs ได้ว่า app.MapProductEndpoints()
public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        // ── พื้นฐาน Minimal API ──────────────────────────────────────────
        // MapGet("/path", (...) => ...) = เมื่อมี GET มาที่ path นี้ ให้รันฟังก์ชันนี้
        // parameter ของฟังก์ชันถูกเติมให้อัตโนมัติ (binding):
        //   string sku           → จาก {sku} ใน URL          (Go: r.PathValue("sku"))
        //   CreateProductRequest → จาก JSON body              (Go: json.Decode)
        //   StockService         → จาก DI container           (Go: ส่งเข้า New(...) เอง)
        //   CancellationToken    → ยกเลิกเมื่อ client ตัดสาย  (Go: r.Context())
        // Results.Ok(x) = 200 + JSON · Results.Created = 201
        // error ไม่ต้องจัดการที่นี่: service throw แล้ว Api/ErrorHandling.cs แปลงเป็น status ให้ที่เดียว
        // ─────────────────────────────────────────────────────────────────

        app.MapGet("/healthz", async (StockDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct)
                ? Results.Ok(new { status = "ok" })                 // new { ... } = anonymous object → JSON {"status":"ok"}
                : Results.Json(new { status = "db down" }, statusCode: StatusCodes.Status503ServiceUnavailable));

        // MapGroup = ตั้ง prefix ร่วม · ทุก route ข้างล่างขึ้นต้นด้วย /products
        var products = app.MapGroup("/products");

        products.MapGet("/", async (StockService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        products.MapGet("/{sku}", async (string sku, StockService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(sku, ct)));

        // ✅ FromSql + $"..." → EF แปลง {q} เป็น parameter ข้อมูลไม่มีทางกลายเป็นคำสั่ง
        products.MapGet("/search", async (string q, StockDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Products
                .FromSql($"SELECT * FROM products WHERE name LIKE '%' || {q} || '%'")
                .ToListAsync(ct)));

        products.MapPost("/", async (CreateProductRequest req, StockService svc, CancellationToken ct) =>
        {
            var p = await svc.CreateAsync(req.Sku, req.Name, req.Qty, req.Price, ct);
            return Results.Created($"/products/{p.Sku}", p); // 201 + header Location บอกว่าของใหม่อยู่ที่ไหน
        });

        products.MapPost("/{sku}/receive", async (string sku, MoveRequest req, StockService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReceiveAsync(sku, req.Qty, ct)));

        products.MapPost("/{sku}/issue", async (string sku, MoveRequest req, StockService svc, CancellationToken ct) =>
            Results.Ok(await svc.IssueAsync(sku, req.Qty, ct)));
    }
}
