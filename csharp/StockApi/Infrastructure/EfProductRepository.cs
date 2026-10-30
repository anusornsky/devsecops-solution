// adapter ขาออก: IProductRepository ที่เก็บข้อมูลด้วย EF Core + SQLite
// เทียบ Go: go/internal/store/sqlite.go (Go เขียน SQL เอง · ที่นี่เขียน LINQ ให้ EF แปลงเป็น SQL)
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockApi.Application;
using StockApi.Domain;

namespace StockApi.Infrastructure;

public sealed class EfProductRepository(StockDbContext db) : IProductRepository
{
    // SQLite แจ้ง "PRIMARY KEY ซ้ำ" ด้วยรหัส 1555 (SQLITE_CONSTRAINT_PRIMARYKEY)
    // Go เช็คจากข้อความ "UNIQUE constraint failed" · ที่นี่เช็คจากรหัส — ไม่พังถ้าข้อความเปลี่ยนภาษา/รุ่น
    private const int SqlitePrimaryKeyViolation = 1555;

    public async Task CreateAsync(Product p, CancellationToken ct)
    {
        db.Products.Add(p); // Add = แค่จดไว้ว่า "จะ INSERT" ยังไม่ส่งไปฐานข้อมูล
        try
        {
            await db.SaveChangesAsync(ct); // SaveChanges = ส่งทุกอย่างที่จดไว้ไปเป็น SQL จริง
        }
        // when (...) = exception filter: จับเฉพาะเมื่อเงื่อนไขจริง ที่เหลือปล่อยผ่านไปเป็น 500
        // is SqliteException { ... } = pattern matching: "เป็นชนิดนี้ และ property นี้มีค่านี้"
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqlitePrimaryKeyViolation })
        {
            throw new ProductAlreadyExistsException();
        }
    }

    // AsNoTracking = อ่านอย่างเดียว ไม่ต้องให้ EF จำ object ไว้เทียบตอน SaveChanges (เร็วกว่า)
    // FirstOrDefaultAsync = แถวแรก หรือ null ถ้าไม่มี · ?? throw = ถ้า null ให้ throw
    public async Task<Product> GetAsync(string sku, CancellationToken ct) =>
        await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Sku == sku, ct)
        ?? throw new ProductNotFoundException();

    // ToListAsync บน 0 แถว = list ว่าง (ไม่ใช่ null) → JSON เป็น [] เอง · Go ต้องระวังเอง (nil slice → null)
    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct) =>
        await db.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync(ct);

    // ExecuteUpdateAsync = UPDATE ตรงในคำสั่งเดียว ไม่โหลด object ขึ้นมาก่อน
    // SQL ที่ได้: UPDATE products SET qty = qty + @qty WHERE sku = @sku
    // ทางที่ไม่เลือก: โหลด p ขึ้นมา → p.Qty += qty → SaveChanges · คำขอพร้อมกัน 2 ตัวจะเขียนทับกัน ยอดหาย
    public async Task<Product> ReceiveAsync(string sku, int qty, CancellationToken ct)
    {
        var rows = await db.Products
            .Where(p => p.Sku == sku)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Qty, p => p.Qty + qty), ct);
        if (rows == 0)
            throw new ProductNotFoundException();
        return await GetAsync(sku, ct);
    }

    // หัวใจของระบบ: เบิกแบบ atomic — เงื่อนไข "qty >= ที่จะเบิก" อยู่ใน UPDATE เดียวกัน
    // SQL ที่ได้: UPDATE products SET qty = qty - @qty WHERE sku = @sku AND qty >= @qty
    // ฐานข้อมูลทำทั้งคำสั่งทีเดียว → 20 คำขอพร้อมกันเบิกจากของ 10 ชิ้น สำเร็จได้แค่ 10 (มีเทสต์พิสูจน์)
    // แก้ 0 แถว = ไม่มีสินค้า หรือ สต็อกไม่พอ → อ่านซ้ำเพื่อแยกว่าเป็นกรณีไหน
    public async Task<Product> IssueAsync(string sku, int qty, CancellationToken ct)
    {
        var rows = await db.Products
            .Where(p => p.Sku == sku && p.Qty >= qty)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Qty, p => p.Qty - qty), ct);
        if (rows == 0)
        {
            await GetAsync(sku, ct); // ไม่มีสินค้า → GetAsync throw ProductNotFoundException เอง
            throw new InsufficientStockException();
        }
        return await GetAsync(sku, ct);
    }
}
