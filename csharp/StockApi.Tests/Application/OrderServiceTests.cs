// เทียบ Go: go/internal/app/orders_test.go
using Microsoft.Extensions.Logging.Abstractions;
using StockApi.Application;
using StockApi.Domain;

namespace StockApi.Tests.Application;

// ของปลอม มี order เดียว · fulfilErrors = exception ที่ FulfilAsync จะโยนทีละครั้งตามลำดับ (หมดแล้ว = สำเร็จ)
// จดทุกการเรียกไว้ใน Calls ให้เทสต์เช็คว่า service สั่งอะไรไปบ้าง
// ใช้ string แทน object Order จริง เพราะ Order แก้ค่าจากข้างนอกไม่ได้ (private set) — ของปลอมเก็บแค่สถานะที่เทสต์ต้องดู
internal sealed class FakeOrders(params Exception[] fulfilErrors) : IOrderRepository
{
    private readonly Queue<Exception> _errors = new(fulfilErrors); // Queue = เข้าก่อนออกก่อน · Dequeue หยิบตัวแรก
    private Order? _order;
    public string Status { get; private set; } = "";
    public int Attempts { get; private set; }
    public List<string> Calls { get; } = [];

    public Task<Order> CreateAsync(Order order, CancellationToken ct)
    {
        _order = order;
        Status = OrderStatus.Pending;
        return Task.FromResult(order);
    }

    public Task<Order> GetAsync(long id, CancellationToken ct) => Task.FromResult(_order!); // ! = "ฉันรับรองว่าไม่ null"

    public Task<Order?> ClaimNextAsync(CancellationToken ct)
    {
        if (Status != OrderStatus.Pending)
            return Task.FromResult<Order?>(null);
        Status = OrderStatus.Processing;
        Attempts++;
        return Task.FromResult<Order?>(WithAttempts(_order!, Attempts));
    }

    public Task<long> FulfilAsync(long id, CancellationToken ct)
    {
        if (_errors.TryDequeue(out var ex))
            throw ex;
        Status = OrderStatus.Done;
        return Task.FromResult(3000L);
    }

    public Task MarkFailedAsync(long id, string reason, CancellationToken ct)
    {
        Status = OrderStatus.Failed;
        Calls.Add("failed");
        return Task.CompletedTask;
    }

    public Task RequeueAsync(long id, TimeSpan delay, CancellationToken ct)
    {
        Status = OrderStatus.Pending;
        Calls.Add($"requeue {delay.TotalSeconds}s");
        return Task.CompletedTask;
    }

    public Task<int> RecoverStuckAsync(CancellationToken ct) => Task.FromResult(0);

    // Order ตั้ง Attempts จากข้างนอกไม่ได้ → ของปลอมใช้ reflection แอบเซ็ต (ในโค้ดจริง EF ทำแบบเดียวกัน)
    private static Order WithAttempts(Order o, int attempts)
    {
        typeof(Order).GetProperty(nameof(Order.Attempts))!.SetValue(o, attempts);
        return o;
    }
}

public sealed class OrderServiceTests
{
    private readonly CancellationToken _ct = CancellationToken.None;

    private async Task<(OrderService, FakeOrders)> PlaceOne(params Exception[] fulfilErrors)
    {
        var repo = new FakeOrders(fulfilErrors);
        var svc = new OrderService(repo, NullLogger<OrderService>.Instance);
        await svc.PlaceAsync([new("PEN-001", 2)], _ct);
        return (svc, repo);
    }

    // เรียก ProcessNextAsync จนคิวว่าง (ของปลอมไม่สนเวลาหน่วง)
    private async Task Drain(OrderService svc)
    {
        for (var i = 0; i < 10; i++)
            if (!await svc.ProcessNextAsync(_ct))
                return;
        Assert.Fail("ProcessNextAsync ไม่หยุดภายใน 10 รอบ");
    }

    [Fact]
    public async Task ProcessNextMarksDoneOnSuccess()
    {
        var (svc, repo) = await PlaceOne();

        await Drain(svc);

        Assert.Equal(OrderStatus.Done, repo.Status);
    }

    // สต็อกไม่พอ = ล้มเหลวถาวร → failed ทันที ไม่เสียเวลาลองใหม่
    [Fact]
    public async Task InsufficientStockFailsWithoutRetry()
    {
        var (svc, repo) = await PlaceOne(new InsufficientStockException("PEN-001 (มี 1 ต้องการ 2)"));

        await Drain(svc);

        Assert.Equal((OrderStatus.Failed, 1), (repo.Status, repo.Attempts));
    }

    // ยอดรวมล้น = ล้มเหลวถาวร (ราคา/จำนวนไม่เปลี่ยนเอง ลองใหม่ก็ล้นอีก) → failed ทันที
    [Fact]
    public async Task TotalTooLargeFailsWithoutRetry()
    {
        var (svc, repo) = await PlaceOne(new OrderTotalTooLargeException());

        await Drain(svc);

        Assert.Equal((OrderStatus.Failed, 1), (repo.Status, repo.Attempts));
    }

    // พังชั่วคราว 1 ครั้งแล้วหาย → ลองใหม่ (หน่วง 2 วิ) แล้วสำเร็จ
    [Fact]
    public async Task TransientErrorIsRetriedThenSucceeds()
    {
        var (svc, repo) = await PlaceOne(new InvalidOperationException("database is locked"));

        await Drain(svc);

        Assert.Equal((OrderStatus.Done, 2), (repo.Status, repo.Attempts));
        Assert.Equal(["requeue 2s"], repo.Calls);
    }

    // พังชั่วคราวทุกครั้ง → ลองครบ MaxAttempts แล้ว failed (ไม่วนลองตลอดไป)
    [Fact]
    public async Task GivesUpAfterMaxAttempts()
    {
        var boom = new IOException("disk I/O error");
        var (svc, repo) = await PlaceOne(boom, boom, boom, boom);

        await Drain(svc);

        Assert.Equal((OrderStatus.Failed, OrderService.MaxAttempts), (repo.Status, repo.Attempts));
        Assert.Equal(["requeue 2s", "requeue 4s", "failed"], repo.Calls);
    }
}
