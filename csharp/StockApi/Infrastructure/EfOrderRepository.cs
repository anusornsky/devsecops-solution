// เก็บคำสั่งซื้อด้วย EF Core — เทียบ Go: go/internal/store/orders.go
using Microsoft.EntityFrameworkCore;
using StockApi.Application;
using StockApi.Domain;

namespace StockApi.Infrastructure;

public sealed class EfOrderRepository(StockDbContext db) : IOrderRepository
{
    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // SaveChanges ครั้งเดียว = INSERT order + ทุกรายการใน transaction เดียวให้เอง (EF ห่อให้อัตโนมัติ)
    // Go ต้องเปิด transaction เองแล้ว INSERT ทีละแถว
    public async Task<Order> CreateAsync(Order order, CancellationToken ct)
    {
        db.Orders.Add(order);
        // RunAt มี private set → domain แก้ไม่ได้ แต่ EF เซ็ตผ่าน Entry ได้ (เวลาเป็นเรื่องของที่เก็บข้อมูล ไม่ใช่กฎธุรกิจ)
        db.Entry(order).Property(o => o.RunAt).CurrentValue = NowMs();
        await db.SaveChangesAsync(ct); // หลังบรรทัดนี้ order.Id และ item.OrderId มีค่าแล้ว
        return order;
    }

    // Include = ดึงรายการลูก (order_items) มาพร้อมกัน · ไม่ใส่ = Items ว่าง
    // OrderBy ใน Include = เรียงรายการตาม sku ให้ตรงกับฝั่ง Go
    public async Task<Order> GetAsync(long id, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Include(o => o.Items.OrderBy(i => i.Sku))
            .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new OrderNotFoundException();

    // หา order รอคิวเก่าสุด → "จอง" ด้วย UPDATE ที่มีเงื่อนไข Status == Pending
    // เทคนิคเดียวกับเบิกสต็อก: worker อื่นจองตัดหน้า → แก้ได้ 0 แถว → วนหาตัวถัดไป
    public async Task<Order?> ClaimNextAsync(CancellationToken ct)
    {
        while (true)
        {
            var now = NowMs();
            // (long?) = แปลงเป็นชนิดที่เป็น null ได้ → ไม่มีแถว FirstOrDefault คืน null (ไม่ใช่ 0 ที่อาจเป็น id จริง)
            var id = await db.Orders
                .Where(o => o.Status == OrderStatus.Pending && o.RunAt <= now)
                .OrderBy(o => o.Id)
                .Select(o => (long?)o.Id)
                .FirstOrDefaultAsync(ct);
            if (id is null)
                return null; // คิวว่าง

            var claimed = await db.Orders
                .Where(o => o.Id == id && o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.Processing)
                    .SetProperty(o => o.Attempts, o => o.Attempts + 1), ct);
            if (claimed == 1)
                return await GetAsync(id.Value, ct);
            // claimed == 0: มีคนจองตัดหน้า → วนหาตัวถัดไป
        }
    }

    // หัวใจของระบบ order: ทุกขั้นอยู่ใน transaction เดียว
    //   1. ตัดสต็อกทีละรายการ (Where ... && p.Qty >= qty เหมือน IssueAsync)
    //   2. จำราคาลง order_items
    //   3. order → done + ยอดรวม
    // รายการไหนไม่ผ่าน → throw → await using ปิด tx โดยไม่ Commit = rollback คืนสต็อกรายการก่อนหน้าทั้งหมด
    // ทางที่ไม่เลือก: ตัดทีละรายการแยกกัน แล้วเขียนโค้ด "คืนสต็อก" เองตอนพัง — ถ้าโปรแกรมตายระหว่างคืน ของหายถาวร
    // Postgres เปิด EnableRetryOnFailure (Program.cs) → เปิด transaction เองตรงๆ ไม่ได้ (throw "does not support user-initiated transactions")
    // ต้องให้ strategy ครอบทั้งก้อน: error ชั่วคราวกลางทาง → rollback แล้วเริ่มก้อนใหม่ทั้งก้อน (ไม่ใช่ retry ทีละคำสั่งจนตัดสต็อกซ้ำ)
    // SQLite ไม่ได้เปิด retry → strategy เป็นแบบรันครั้งเดียว ทำงานเหมือนเดิม
    public Task<long> FulfilAsync(long id, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => FulfilOnceAsync(id, ct));

    private async Task<long> FulfilOnceAsync(long id, CancellationToken ct)
    {
        // ExecuteUpdateAsync ทุกตัวข้างล่างวิ่งใน transaction นี้ (EF ใช้ transaction ที่เปิดค้างไว้บน connection ให้เอง)
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var items = await db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == id)
            .OrderBy(i => i.Sku)
            .ToListAsync(ct);

        long total = 0;
        foreach (var it in items)
        {
            // copy ลงตัวแปรก่อน: EF แปลงตัวแปรธรรมดาเป็น parameter ของ SQL ได้ตรงไปตรงมา
            var sku = it.Sku;
            var qty = it.Qty;

            var rows = await db.Products
                .Where(p => p.Sku == sku && p.Qty >= qty)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Qty, p => p.Qty - qty), ct);
            if (rows == 0)
                throw await WhyNotIssuedAsync(sku, qty, ct);

            var price = await db.Products.Where(p => p.Sku == sku).Select(p => p.Price).FirstAsync(ct);
            await db.OrderItems
                .Where(i => i.OrderId == id && i.Sku == sku)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UnitPrice, price), ct);
            // checked = ถ้าคูณ/บวกเกินที่ long เก็บได้ให้ throw OverflowException แทนวนเป็นเลขติดลบเงียบๆ (ค่าเริ่มต้นของ C# คือไม่เช็ค)
            try
            {
                total = checked(total + price * qty);
            }
            catch (OverflowException)
            {
                throw new OrderTotalTooLargeException();
            }
        }

        // Status == Processing = กันพลาด: ถ้ามีใครเปลี่ยนสถานะไปแล้ว อย่าทับ (throw → rollback ทั้งหมด)
        var done = await db.Orders
            .Where(o => o.Id == id && o.Status == OrderStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Done)
                .SetProperty(o => o.Total, total)
                .SetProperty(o => o.Reason, ""), ct);
        if (done != 1)
            throw new InvalidOperationException($"order {id} ไม่ได้อยู่ในสถานะ processing แล้ว");

        await tx.CommitAsync(ct);
        return total;
    }

    // แยกว่าตัดไม่ได้เพราะไม่มีสินค้า หรือของไม่พอ — ข้อความนี้จะไปอยู่ใน reason ให้ลูกค้าเห็น
    private async Task<StockException> WhyNotIssuedAsync(string sku, int qty, CancellationToken ct)
    {
        var have = await db.Products.Where(p => p.Sku == sku).Select(p => (int?)p.Qty).FirstOrDefaultAsync(ct);
        return have is null
            ? new ProductNotFoundException(sku)
            : new InsufficientStockException($"{sku} (มี {have} ต้องการ {qty})");
    }

    public Task MarkFailedAsync(long id, string reason, CancellationToken ct) =>
        MoveFromProcessingAsync(id, q => q.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, OrderStatus.Failed)
            .SetProperty(o => o.Reason, reason), ct));

    public Task RequeueAsync(long id, TimeSpan delay, CancellationToken ct)
    {
        var runAt = NowMs() + (long)delay.TotalMilliseconds;
        return MoveFromProcessingAsync(id, q => q.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, OrderStatus.Pending)
            .SetProperty(o => o.RunAt, runAt), ct));
    }

    // เปลี่ยนสถานะเฉพาะ order ที่ยัง processing อยู่ · Func<..., Task<int>> = รับ "ฟังก์ชัน" มาเป็น parameter (Go ส่ง string set มาแทน)
    private async Task MoveFromProcessingAsync(long id, Func<IQueryable<Order>, Task<int>> update)
    {
        var rows = await update(db.Orders.Where(o => o.Id == id && o.Status == OrderStatus.Processing));
        if (rows != 1)
            throw new InvalidOperationException($"order {id} ไม่ได้อยู่ในสถานะ processing แล้ว");
    }

    // เรียกตอนเปิดโปรแกรม: order ที่ค้าง processing = รอบก่อนตายกลางงาน (transaction ถูก rollback แล้ว) → คืนเข้าคิว
    // ponytail: ถือว่ามี worker ตัวเดียว · รันหลายเครื่องจะแย่งงานที่อีกเครื่องกำลังทำ → ตอนนั้นค่อยเพิ่ม lease (จองถึงเวลา)
    public Task<int> RecoverStuckAsync(CancellationToken ct) =>
        db.Orders
            .Where(o => o.Status == OrderStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Pending), ct);
}
