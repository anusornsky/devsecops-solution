// DbContext = ตัวแทน "ฐานข้อมูล 1 ก้อน" ของ EF Core: บอกว่ามีตารางอะไร คอลัมน์ชื่ออะไร กฎอะไร
// เทียบ Go: ค่าคงที่ schema (CREATE TABLE ...) ใน go/internal/store/sqlite.go — Go เขียน SQL เอง · C# ให้ EF สร้าง SQL จากโค้ดนี้
using Microsoft.EntityFrameworkCore;
using StockApi.Domain;

namespace StockApi.Infrastructure;

// ── พื้นฐาน EF Core ──────────────────────────────────────────────────────
// : DbContext(options) = สืบทอดจาก DbContext · options (ใช้ SQLite ไฟล์ไหน) ส่งมาจาก Program.cs ผ่าน DI
// DbSet<Product>       = "ตาราง products" ที่เขียน LINQ ถามได้ เช่น db.Products.Where(p => p.Qty > 0)
//                         EF แปลง LINQ เป็น SQL ให้ → ส่งไปฐานข้อมูล → แปลงแถวกลับเป็น Product
// => Set<Product>()    = property ที่คืนตารางทุกครั้งที่เรียก (ไม่ต้องมี setter)
// ─────────────────────────────────────────────────────────────────────────
public sealed class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    // OnModelCreating = ที่ตั้งค่าแผนที่ "class ↔ ตาราง" (Fluent API) · EF เรียกเองครั้งเดียวตอนเริ่ม
    // ตั้งชื่อตาราง/คอลัมน์ให้ตรงกับฝั่ง Go ทุกตัว → ไฟล์ .db ของ 2 ภาษาหน้าตาเหมือนกัน
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>(e =>
        {
            // CHECK (qty >= 0) = ฐานข้อมูลกันติดลบอีกชั้น แม้โค้ดจะพลาด
            e.ToTable("products", t => t.HasCheckConstraint("ck_products_qty", "qty >= 0"));
            e.HasKey(p => p.Sku);                    // PRIMARY KEY → sku ซ้ำไม่ได้
            e.Property(p => p.Sku).HasColumnName("sku");
            e.Property(p => p.Name).HasColumnName("name").IsRequired();
            e.Property(p => p.Qty).HasColumnName("qty");
            e.Property(p => p.Price).HasColumnName("price");
        });
        // ⚠️ ไฟล์ stock.db ที่สร้างก่อนมีช่อง price ต้องลบทิ้ง — EnsureCreated ไม่แก้ตารางที่มีอยู่แล้ว
        // (นี่คือเหตุผลที่งานจริงต้องใช้ migration: สั่ง "เพิ่มคอลัมน์" กับฐานข้อมูลที่มีข้อมูลอยู่แล้วได้)
        b.Entity<Product>().ToTable(t => t.HasCheckConstraint("ck_products_price", "price >= 0"));

        b.Entity<Order>(e =>
        {
            e.ToTable("orders", t => t.HasCheckConstraint("ck_orders_status",
                "status IN ('pending', 'processing', 'done', 'failed')"));
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.Status).HasColumnName("status");
            e.Property(o => o.Total).HasColumnName("total");
            e.Property(o => o.Reason).HasColumnName("reason");
            e.Property(o => o.Attempts).HasColumnName("attempts");
            e.Property(o => o.RunAt).HasColumnName("run_at");
            // worker ถาม "pending ที่ถึงเวลาแล้ว ORDER BY id" ทุก 500ms · ไม่มี index = สแกนทุก order ที่เคยมี · ชื่อ/คอลัมน์ตรงกับ Go
            // (status, id) ไม่ใส่ run_at: ได้ id เรียงมาจาก index เลย ไม่ต้องเรียงใหม่ (เหตุผลเต็มดู go/internal/store/sqlite.go)
            e.HasIndex(o => new { o.Status, o.Id }).HasDatabaseName("idx_orders_queue");
            // 1 order มีหลายรายการ · ไม่ใส่ WithOne(...) ฝั่ง item เพราะ item ไม่ต้องรู้จัก order (ไม่มี property ย้อนกลับ)
            e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
        });

        b.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items", t => t.HasCheckConstraint("ck_order_items_qty", "qty > 0"));
            e.HasKey(i => new { i.OrderId, i.Sku }); // key 2 คอลัมน์ (composite) · new { } = anonymous type บอกว่าคอลัมน์ไหนบ้าง
            e.Property(i => i.OrderId).HasColumnName("order_id");
            e.Property(i => i.Sku).HasColumnName("sku"); // ไม่ผูก FK กับ products: สั่ง SKU ที่ไม่มีได้ worker จะตีเป็น failed
            e.Property(i => i.Qty).HasColumnName("qty");
            e.Property(i => i.UnitPrice).HasColumnName("unit_price"); // จำราคาตอนตัดสต็อก · ขึ้นราคาทีหลังบิลเก่าไม่เปลี่ยน
        });
    }
}
