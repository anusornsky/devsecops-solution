// port ขาออกของ order · เทียบ Go: interface OrderRepository ใน go/internal/app/orders.go
// ทุก method ที่เปลี่ยนสถานะต้องเช็คสถานะเดิมใน WHERE (กันสองคนแก้ทับกัน)
using StockApi.Domain;

namespace StockApi.Application;

public interface IOrderRepository
{
    Task<Order> CreateAsync(Order order, CancellationToken ct);  // คืน order ที่มี Id แล้ว
    Task<Order> GetAsync(long id, CancellationToken ct);         // ไม่มี → OrderNotFoundException
    // ClaimNextAsync หยิบ order pending ที่ถึงเวลาแล้ว 1 ตัว → processing + attempts+1 · ไม่มีงาน = null
    Task<Order?> ClaimNextAsync(CancellationToken ct);
    // FulfilAsync ตัดสต็อกทุกรายการ + จำราคา + done ใน transaction เดียว (สำเร็จทั้งหมด หรือไม่เปลี่ยนอะไรเลย)
    // สินค้าไม่มี / ไม่พอ → ProductNotFoundException / InsufficientStockException
    Task<long> FulfilAsync(long id, CancellationToken ct);
    Task MarkFailedAsync(long id, string reason, CancellationToken ct);
    Task RequeueAsync(long id, TimeSpan delay, CancellationToken ct); // กลับไป pending แต่ห้ามหยิบจนครบ delay
    Task<int> RecoverStuckAsync(CancellationToken ct);                // processing ค้าง (โปรแกรมตายกลางงาน) → pending
}
