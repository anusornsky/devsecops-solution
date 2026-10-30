// เทียบ Go: go/internal/worker/worker_test.go
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockApi.Application;
using StockApi.Domain;
using StockApi.Worker;

namespace StockApi.Tests.Worker;

// ของปลอมที่ฐานข้อมูล "พัง" ตอนหยิบงานทุกครั้ง · นับว่าถูกสั่งคืนงานค้างกี่ครั้ง
// Interlocked = บวกตัวนับจากหลาย thread ได้ปลอดภัย (worker วิ่งคนละ thread กับเทสต์) · Go: atomic.Int32
internal sealed class BrokenOrders : IOrderRepository
{
    private int _recovers;
    public int Recovers => Volatile.Read(ref _recovers);

    public Task<Order?> ClaimNextAsync(CancellationToken ct) => throw new InvalidOperationException("database is locked");

    public Task<int> RecoverStuckAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _recovers);
        return Task.FromResult(0);
    }

    public Task<Order> CreateAsync(Order order, CancellationToken ct) => throw new NotSupportedException();
    public Task<Order> GetAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<long> FulfilAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task MarkFailedAsync(long id, string reason, CancellationToken ct) => throw new NotSupportedException();
    public Task RequeueAsync(long id, TimeSpan delay, CancellationToken ct) => throw new NotSupportedException();
}

public sealed class OrderWorkerTests
{
    // ProcessNext พัง = order อาจค้าง processing (ย้ายสถานะไม่สำเร็จ) → รอบถัดไปต้องคืนงานค้างเข้าคิว ไม่ใช่รอรีสตาร์ท
    [Fact]
    public async Task RunRecoversStuckAfterError()
    {
        // Arrange: ประกอบ DI เท่าที่ worker ใช้ (Program.cs ประกอบตัวจริง)
        var repo = new BrokenOrders();
        await using var services = new ServiceCollection()
            .AddSingleton<IOrderRepository>(repo)
            .AddScoped(sp => new OrderService(sp.GetRequiredService<IOrderRepository>(), NullLogger<OrderService>.Instance))
            .BuildServiceProvider();
        OrderWorker.Interval = TimeSpan.FromMilliseconds(10);
        var worker = new OrderWorker(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OrderWorker>.Instance);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(repo.Recovers > 0, "recovers = 0, want ≥ 1 หลัง ProcessNext พัง");
    }
}
