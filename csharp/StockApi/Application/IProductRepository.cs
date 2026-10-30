// port ขาออก: "ที่เก็บสินค้า" — StockService รู้จักแค่ interface นี้ ไม่รู้ว่าข้างหลังเป็น EF Core หรือของปลอมในเทสต์
// เทียบ Go: interface Repository ใน go/internal/app/service.go
using StockApi.Domain;

namespace StockApi.Application;

// ── พื้นฐาน ──────────────────────────────────────────────────────────────
// interface = "สัญญา" ว่าต้องมี method อะไรบ้าง ไม่มีโค้ดข้างใน · คลาสที่ implement ต้องเขียนให้ครบ
//   C#: ต้องประกาศ class X : IProductRepository ตรงๆ
//   Go: ไม่ต้องประกาศ — มี method ครบก็นับว่า implement แล้ว (implicit)
// Task<T>   = "ผลลัพธ์ที่จะมาทีหลัง" ของงาน async (รอ I/O เช่นฐานข้อมูล) · เรียกด้วย await
// CancellationToken ct = ตัวบอกว่า "เลิกทำได้แล้ว" (client ตัดสาย/ปิดโปรแกรม) · Go ใช้ context.Context
// ─────────────────────────────────────────────────────────────────────────
public interface IProductRepository
{
    Task CreateAsync(Product p, CancellationToken ct);                // มีแล้ว → ProductAlreadyExistsException
    Task<Product> GetAsync(string sku, CancellationToken ct);         // ไม่มี → ProductNotFoundException
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct);     // IReadOnlyList = list ที่คนรับแก้ไม่ได้
    Task<Product> ReceiveAsync(string sku, int qty, CancellationToken ct);
    // IssueAsync ต้องกันเบิกเกินแบบ atomic (ทำในคำสั่งเดียวที่ฐานข้อมูล) → InsufficientStockException
    Task<Product> IssueAsync(string sku, int qty, CancellationToken ct);
}
