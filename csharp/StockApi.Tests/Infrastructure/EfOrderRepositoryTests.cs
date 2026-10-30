// เทียบ Go: go/internal/store/orders_test.go · SQLite จริงในไฟล์ชั่วคราว
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockApi.Domain;
using StockApi.Infrastructure;

namespace StockApi.Tests.Infrastructure;

public sealed class EfOrderRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"order-test-{Guid.NewGuid()}.db");
    private readonly CancellationToken _ct = CancellationToken.None;

    public EfOrderRepositoryTests()
    {
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    private StockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<StockDbContext>().UseSqlite($"Data Source={_path}").Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    // Seed สร้างสินค้า + order 1 ตัว แล้วจองมาให้พร้อม Fulfil · คืน id
    private async Task<long> Seed(Product[] products, OrderItemInput[] items)
    {
        await using var db = NewDb();
        db.Products.AddRange(products);
        await db.SaveChangesAsync(_ct);
        var repo = new EfOrderRepository(db);
        var order = await repo.CreateAsync(Order.Create(items), _ct);
        Assert.NotNull(await repo.ClaimNextAsync(_ct));
        return order.Id;
    }

    private async Task<int> QtyOf(string sku)
    {
        await using var db = NewDb();
        return await db.Products.Where(p => p.Sku == sku).Select(p => p.Qty).SingleAsync(_ct);
    }

    // ทุกขั้นใช้ DbContext ใหม่ = เหมือนคนละ request (ไม่มีข้อมูลค้างในหน่วยความจำมาหลอกผลเทสต์)
    private async Task<T> WithRepo<T>(Func<EfOrderRepository, Task<T>> act)
    {
        await using var db = NewDb();
        return await act(new EfOrderRepository(db));
    }

    [Fact]
    public async Task FulfilDeductsStockAndSnapshotsPrice()
    {
        var id = await Seed(
            [new("PEN-001", "ปากกา", 10, 1500), new("CUP-001", "แก้ว", 5, 5000)],
            [new("PEN-001", 2), new("CUP-001", 1)]);

        var total = await WithRepo(r => r.FulfilAsync(id, _ct));

        Assert.Equal(2 * 1500 + 5000, total);
        Assert.Equal((8, 4), (await QtyOf("PEN-001"), await QtyOf("CUP-001")));
        var o = await WithRepo(r => r.GetAsync(id, _ct));
        Assert.Equal(OrderStatus.Done, o.Status);
        Assert.Equal(1500, o.Items[1].UnitPrice); // เรียงตาม sku: CUP-001, PEN-001
    }

    // เทสต์ที่สำคัญที่สุดของระบบ order: รายการที่ 2 ไม่พอ → รายการที่ 1 ต้องไม่ถูกตัด (transaction rollback)
    [Fact]
    public async Task FulfilIsAllOrNothing()
    {
        var id = await Seed(
            [new("AAA-001", "ตัดได้", 10, 0), new("ZZZ-001", "ไม่พอ", 1, 0)],
            [new("AAA-001", 3), new("ZZZ-001", 5)]); // AAA ถูกตัดก่อน (เรียงตาม sku)

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => WithRepo(r => r.FulfilAsync(id, _ct)));

        Assert.Equal(10, await QtyOf("AAA-001")); // ต้องถูกคืนเพราะ rollback
        Assert.Equal("สต็อกไม่พอ: ZZZ-001 (มี 1 ต้องการ 5)", ex.Message);
    }

    // ยอดรวมเกินที่ long เก็บได้ → ต้องปฏิเสธ (เดิมวนเป็น -2 แล้วบันทึก done เงียบๆ) และคืนสต็อก
    [Fact]
    public async Task FulfilRejectsTotalOverflow()
    {
        var id = await Seed([new("GOLD-001", "ทองแท่ง", 5, long.MaxValue)], [new("GOLD-001", 2)]);

        await Assert.ThrowsAsync<OrderTotalTooLargeException>(() => WithRepo(r => r.FulfilAsync(id, _ct)));

        Assert.Equal(5, await QtyOf("GOLD-001")); // ต้องถูกคืนเพราะ rollback
    }

    [Fact]
    public async Task FulfilUnknownSkuIsNotFound()
    {
        var id = await Seed([], [new("NOPE-1", 1)]);

        await Assert.ThrowsAsync<ProductNotFoundException>(() => WithRepo(r => r.FulfilAsync(id, _ct)));
    }

    // จองซ้ำไม่ได้: order เดียวต้องถูกหยิบได้ครั้งเดียว
    [Fact]
    public async Task ClaimNextTakesEachOrderOnce()
    {
        await Seed([], [new("PEN-001", 1)]); // Seed จองไปแล้ว 1 ครั้ง

        Assert.Null(await WithRepo(r => r.ClaimNextAsync(_ct)));
    }

    // Requeue พร้อมหน่วงเวลา → ยังหยิบไม่ได้จนกว่าจะถึงเวลา
    [Fact]
    public async Task RequeuedOrderWaitsForDelay()
    {
        var id = await Seed([], [new("PEN-001", 1)]);

        await WithRepo(async r => { await r.RequeueAsync(id, TimeSpan.FromHours(1), _ct); return 0; });

        Assert.Null(await WithRepo(r => r.ClaimNextAsync(_ct)));
        await Assert.ThrowsAsync<InvalidOperationException>( // ไม่ได้ processing แล้ว ต้องไม่ยอม
            () => WithRepo(async r => { await r.RequeueAsync(id, TimeSpan.Zero, _ct); return 0; }));
    }

    // โปรแกรมตายตอน processing → เปิดใหม่ต้องได้งานคืนเข้าคิว
    [Fact]
    public async Task RecoverStuckReturnsProcessingToQueue()
    {
        await Seed([], [new("PEN-001", 1)]);

        Assert.Equal(1, await WithRepo(r => r.RecoverStuckAsync(_ct)));
        Assert.NotNull(await WithRepo(r => r.ClaimNextAsync(_ct)));
    }
}
