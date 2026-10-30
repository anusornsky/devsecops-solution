// เทียบ Go: go/internal/app/service_test.go
// เทสต์ service โดยไม่แตะฐานข้อมูลเลย — ใช้ที่เก็บปลอมในหน่วยความจำแทน (เร็ว · ไม่ต้องมีไฟล์)
using Microsoft.Extensions.Logging.Abstractions;
using StockApi.Application;
using StockApi.Domain;

namespace StockApi.Tests.Application;

// ของปลอม (fake) เขียนเองไม่กี่บรรทัด แทนการใช้ library mock (Moq) — อ่านง่ายกว่า และเห็นพฤติกรรมชัด
// Dictionary<string, Product> = ตาราง key → value · Go: map[string]domain.Product
// Task.FromResult(x) = ห่อค่าที่มีอยู่แล้วเป็น Task (เพราะ interface บังคับให้คืน Task แต่ของปลอมไม่ต้องรออะไร)
internal sealed class MemRepo : IProductRepository
{
    private readonly Dictionary<string, Product> _items = new();

    public Task CreateAsync(Product p, CancellationToken ct)
    {
        if (!_items.TryAdd(p.Sku, p)) // TryAdd = เพิ่มถ้ายังไม่มี คืน false ถ้ามีแล้ว
            throw new ProductAlreadyExistsException();
        return Task.CompletedTask;
    }

    public Task<Product> GetAsync(string sku, CancellationToken ct) =>
        _items.TryGetValue(sku, out var p) ? Task.FromResult(p) : throw new ProductNotFoundException();

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Product>>(_items.Values.ToList());

    public async Task<Product> ReceiveAsync(string sku, int qty, CancellationToken ct)
    {
        var p = await GetAsync(sku, ct);
        return _items[sku] = p with { Qty = p.Qty + qty }; // with = สำเนา record ที่เปลี่ยนบางช่อง (ตัวเดิมไม่ถูกแก้)
    }

    public async Task<Product> IssueAsync(string sku, int qty, CancellationToken ct)
    {
        var p = await GetAsync(sku, ct);
        if (p.Qty < qty)
            throw new InsufficientStockException();
        return _items[sku] = p with { Qty = p.Qty - qty };
    }
}

public sealed class StockServiceTests
{
    // NullLogger = logger ที่ทิ้งทุกอย่าง (เทสต์ไม่ต้องการ log) · Go: slog.DiscardHandler
    private readonly MemRepo _repo = new();
    private readonly StockService _svc;
    private readonly CancellationToken _ct = CancellationToken.None;

    // xUnit สร้าง object ใหม่ทุกเทสต์ → แต่ละเทสต์ได้ MemRepo ว่างของตัวเอง ไม่ปนกัน
    public StockServiceTests() => _svc = new StockService(_repo, NullLogger<StockService>.Instance);

    [Fact]
    public async Task CreateRejectsInvalidProductBeforeSaving()
    {
        await Assert.ThrowsAsync<InvalidProductException>(() => _svc.CreateAsync("x", "ปากกา", 1, 0, _ct));

        Assert.Empty(await _repo.ListAsync(_ct)); // ไม่มีอะไรหลุดลงที่เก็บ
    }

    [Fact]
    public async Task ReceiveThenIssueUpdatesBalance()
    {
        // Arrange
        await _svc.CreateAsync("PEN-001", "ปากกา", 5, 1500, _ct);

        // Act
        await _svc.ReceiveAsync("PEN-001", 10, _ct);
        var p = await _svc.IssueAsync("PEN-001", 12, _ct);

        // Assert
        Assert.Equal(3, p.Qty);
    }

    [Fact]
    public async Task IssueMoreThanBalanceFailsAndKeepsBalance()
    {
        await _svc.CreateAsync("PEN-001", "ปากกา", 2, 1500, _ct);

        await Assert.ThrowsAsync<InsufficientStockException>(() => _svc.IssueAsync("PEN-001", 3, _ct));

        Assert.Equal(2, (await _svc.GetAsync("PEN-001", _ct)).Qty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MoveWithZeroQtyIsInvalid(int qty)
    {
        await _svc.CreateAsync("PEN-001", "ปากกา", 2, 1500, _ct);

        await Assert.ThrowsAsync<InvalidProductException>(() => _svc.ReceiveAsync("PEN-001", qty, _ct));
        await Assert.ThrowsAsync<InvalidProductException>(() => _svc.IssueAsync("PEN-001", qty, _ct));
    }
}
