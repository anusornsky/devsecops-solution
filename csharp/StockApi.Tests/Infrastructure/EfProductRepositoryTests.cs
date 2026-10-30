// เทียบ Go: go/internal/store/sqlite_test.go
// integration test = ใช้ SQLite จริงในไฟล์ชั่วคราว ไม่ใช้ของปลอม (พิสูจน์ว่า SQL ที่ EF สร้างทำงานจริง)
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockApi.Domain;
using StockApi.Infrastructure;

namespace StockApi.Tests.Infrastructure;

// IDisposable = xUnit เรียก Dispose() ให้เองหลังเทสต์จบ → ใช้ลบไฟล์ (Go: t.TempDir() + t.Cleanup)
public sealed class EfProductRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"stock-test-{Guid.NewGuid()}.db");
    private readonly CancellationToken _ct = CancellationToken.None;

    public EfProductRepositoryTests()
    {
        using var db = NewDb();
        db.Database.EnsureCreated();
    }

    // DbContext ใหม่ทุกครั้งที่เรียก — ใน API จริงก็ได้ตัวใหม่ทุก request เหมือนกัน
    private StockDbContext NewDb() =>
        new(new DbContextOptionsBuilder<StockDbContext>().UseSqlite($"Data Source={_path}").Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // SQLite เก็บ connection ไว้ใช้ซ้ำ (pool) → ต้องปิดก่อน ไม่งั้น Windows ไม่ให้ลบไฟล์
        File.Delete(_path);
    }

    [Fact]
    public async Task CreateTwiceIsAlreadyExists()
    {
        await using var db = NewDb(); // await using = Dispose แบบ async เมื่อจบ method
        var repo = new EfProductRepository(db);
        await repo.CreateAsync(new Product("PEN-001", "ปากกา", 1, 0), _ct);

        await using var db2 = NewDb(); // context ใหม่ เหมือนคำขอที่ 2 จาก client อีกคน
        await Assert.ThrowsAsync<ProductAlreadyExistsException>(
            () => new EfProductRepository(db2).CreateAsync(new Product("PEN-001", "ปากกา", 1, 0), _ct));
    }

    [Fact]
    public async Task IssueDistinguishesNotFoundFromInsufficient()
    {
        await using var db = NewDb();
        var repo = new EfProductRepository(db);
        await repo.CreateAsync(new Product("PEN-001", "ปากกา", 2, 0), _ct);

        await Assert.ThrowsAsync<ProductNotFoundException>(() => repo.IssueAsync("NOPE-1", 1, _ct));
        await Assert.ThrowsAsync<InsufficientStockException>(() => repo.IssueAsync("PEN-001", 3, _ct));
    }

    // เทสต์ที่พิสูจน์ว่า "เบิกแบบ atomic" กันเบิกเกินได้จริงเมื่อมีคำขอพร้อมกัน
    // 20 งานแย่งเบิกทีละ 1 จากสต็อก 10 → ต้องสำเร็จ 10 พอดี และคงเหลือ 0
    // Go: 20 goroutine + WaitGroup · C#: 20 Task + Task.WhenAll
    [Fact]
    public async Task ConcurrentIssueNeverOversells()
    {
        await using (var db = NewDb())
            await new EfProductRepository(db).CreateAsync(new Product("PEN-001", "ปากกา", 10, 0), _ct);

        // Enumerable.Range(0, 20).Select(...) = สร้าง 20 งาน · Task.Run = ส่งไปรันบน thread pool พร้อมกัน
        var attempts = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDb(); // แต่ละงานมี DbContext ของตัวเอง (DbContext ใช้ข้าม thread ไม่ได้)
            try
            {
                await new EfProductRepository(db).IssueAsync("PEN-001", 1, _ct);
                return true;
            }
            catch (InsufficientStockException)
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(attempts); // รอครบทั้ง 20 แล้วได้ bool[] กลับมา

        await using var check = NewDb();
        var p = await new EfProductRepository(check).GetAsync("PEN-001", _ct);
        Assert.Equal(10, results.Count(ok => ok));
        Assert.Equal(0, p.Qty);
    }

    [Fact]
    public async Task ListEmptyIsEmptyNotNull()
    {
        await using var db = NewDb();

        var got = await new EfProductRepository(db).ListAsync(_ct);

        Assert.NotNull(got);
        Assert.Empty(got);
    }
}
