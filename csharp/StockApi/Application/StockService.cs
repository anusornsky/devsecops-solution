// ลำดับงาน (use case) — ตรวจกฎด้วย Domain แล้วสั่งที่เก็บข้อมูลผ่าน IProductRepository
// เทียบ Go: go/internal/app/service.go
using StockApi.Domain;

namespace StockApi.Application;

// ── พื้นฐาน ──────────────────────────────────────────────────────────────
// StockService(IProductRepository repo, ILogger<StockService> log) = primary constructor
//   → DI container (ระบบที่ ASP.NET สร้าง object ให้) เห็นว่าต้องใช้ 2 อย่างนี้ แล้วหามาใส่ให้เอง
//   → เรียกว่า "dependency injection" — คลาสไม่ new ของที่ใช้เอง แต่รับจากข้างนอก จึงเปลี่ยนเป็นของปลอมตอนเทสต์ได้
//   Go: เขียน NewService(repo, log) แล้วส่งเองใน main.go (ไม่มี container)
// async Task<Product> = method ที่ข้างในมี await · คืน Task ให้คนเรียก await ต่อ
// await = "รอผลตรงนี้ แต่ไม่บล็อก thread" — ระหว่างรอฐานข้อมูล thread ไปรับคำขออื่นได้
// ─────────────────────────────────────────────────────────────────────────
public sealed class StockService(IProductRepository repo, ILogger<StockService> log)
{
    // Create ตรวจกฎผ่าน Product.Create ก่อนบันทึก — ข้อมูลผิดกฎไม่ถึงฐานข้อมูลเลย
    public async Task<Product> CreateAsync(string? sku, string? name, int qty, long price, CancellationToken ct)
    {
        var p = Product.Create(sku, name, qty, price); // ผิดกฎ = throw ออกไปเลย บรรทัดล่างไม่ทำ
        await repo.CreateAsync(p, ct);
        // log แบบมีโครงสร้าง: {Sku} {Qty} กลายเป็นช่องใน JSON log (ไม่ใช่แค่ข้อความต่อกัน) — Go: log.Info("..", "sku", p.SKU)
        log.LogInformation("สร้างสินค้า {Sku} {Qty} {Price}", p.Sku, p.Qty, p.Price);
        return p;
    }

    // => = "expression-bodied" ย่อจาก { return ...; } เมื่อ method มีบรรทัดเดียว
    public Task<Product> GetAsync(string sku, CancellationToken ct) => repo.GetAsync(sku, ct);

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct) => repo.ListAsync(ct);

    public async Task<Product> ReceiveAsync(string sku, int qty, CancellationToken ct)
    {
        Product.ValidateMove(qty);
        var p = await repo.ReceiveAsync(sku, qty, ct);
        log.LogInformation("รับเข้า {Sku} {Qty} {Balance}", sku, qty, p.Qty);
        return p;
    }

    // Issue เบิกไม่สำเร็จ log เป็น Warning (ไม่ใช่ระบบพัง แต่น่าจับตา) แล้ว throw ต่อให้ชั้น Api
    public async Task<Product> IssueAsync(string sku, int qty, CancellationToken ct)
    {
        Product.ValidateMove(qty);
        try
        {
            var p = await repo.IssueAsync(sku, qty, ct);
            log.LogInformation("เบิกออก {Sku} {Qty} {Balance}", sku, qty, p.Qty);
            return p;
        }
        catch (StockException ex) // จับเฉพาะ exception ของงานสต็อก · ฐานข้อมูลพังไม่จับ ปล่อยขึ้นไปเป็น 500
        {
            log.LogWarning("เบิกไม่สำเร็จ {Sku} {Qty} {Error}", sku, qty, ex.Message);
            throw; // throw; เฉยๆ = โยนตัวเดิมต่อ (เก็บ stack trace เดิมไว้) · ห้ามเขียน throw ex;
        }
    }
}
