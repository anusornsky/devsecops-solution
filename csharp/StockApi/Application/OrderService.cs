// ลำดับงานของคำสั่งซื้อ: รับ order (เร็ว ตอบทันที) + ประมวลผลทีหลัง (worker เรียก ProcessNextAsync)
// เทียบ Go: go/internal/app/orders.go
using StockApi.Domain;

namespace StockApi.Application;

public sealed class OrderService(IOrderRepository repo, ILogger<OrderService> log)
{
    // ลองได้กี่ครั้งถ้าพังแบบชั่วคราว (ฐานข้อมูลไม่ตอบ ฯลฯ) ก่อนยอมแพ้
    public const int MaxAttempts = 3;

    // PlaceAsync รับ order แล้วตอบทันที (ไม่รอตัดสต็อก) — client ได้ 202 + id ไว้ถามสถานะทีหลัง
    // ทำไมไม่ตัดสต็อกเลยตรงนี้: งานจริงตอนประมวลผลมักมีขั้นช้า (ตัดบัตร, เรียกระบบขนส่ง) → ไม่ให้ client รอ
    public async Task<Order> PlaceAsync(IEnumerable<OrderItemInput>? items, CancellationToken ct)
    {
        var order = await repo.CreateAsync(Order.Create(items), ct);
        log.LogInformation("รับคำสั่งซื้อ {OrderId} {Items}", order.Id, order.Items.Count);
        return order;
    }

    public Task<Order> GetAsync(long id, CancellationToken ct) => repo.GetAsync(id, ct);

    // คืน order ที่ค้าง processing เข้าคิว — worker เรียกหลังรอบที่พัง
    public Task<int> RecoverStuckAsync(CancellationToken ct) => repo.RecoverStuckAsync(ct);

    // ProcessNextAsync ทำ order ถัดไป 1 ตัว · true = ทำไป 1 ตัว (worker ควรเรียกต่อทันที) · false = คิวว่าง
    // exception ที่หลุดออกไป = ระบบมีปัญหา (หยิบงาน/บันทึกสถานะไม่ได้) ไม่ใช่ order ล้มเหลว
    //
    // หัวใจ: แยก "ล้มเหลวถาวร" กับ "ล้มเหลวชั่วคราว"
    //   - สินค้าไม่มี / สต็อกไม่พอ → ลองใหม่กี่ทีก็ไม่ผ่าน → failed ทันที
    //   - อย่างอื่น (ฐานข้อมูลสะดุด) → ลองใหม่ หน่วงเวลาเพิ่มขึ้นทุกครั้ง จนครบ MaxAttempts
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var order = await repo.ClaimNextAsync(ct);
        if (order is null)
            return false;

        try
        {
            var total = await repo.FulfilAsync(order.Id, ct);
            log.LogInformation("คำสั่งซื้อสำเร็จ {OrderId} {Total}", order.Id, total);
        }
        catch (Exception ex) when (ex is ProductNotFoundException or InsufficientStockException or OrderTotalTooLargeException)
        {
            log.LogWarning("คำสั่งซื้อล้มเหลว {OrderId} {Reason}", order.Id, ex.Message);
            await repo.MarkFailedAsync(order.Id, ex.Message, ct);
        }
        catch (Exception ex) when (order.Attempts >= MaxAttempts)
        {
            log.LogError(ex, "ลองครบแล้ว ยอมแพ้ {OrderId} {Attempts}", order.Id, order.Attempts);
            await repo.MarkFailedAsync(order.Id, $"ลองครบ {MaxAttempts} ครั้งแล้ว: {ex.Message}", ct);
        }
        catch (Exception ex) // พังชั่วคราว → backoff: ครั้งที่ 1 รอ 2 วิ · ครั้งที่ 2 รอ 4 วิ
        {
            var delay = TimeSpan.FromSeconds(order.Attempts * 2);
            log.LogWarning("พังชั่วคราว จะลองใหม่ {OrderId} {Attempt} {Delay} {Error}", order.Id, order.Attempts, delay, ex.Message);
            await repo.RequeueAsync(order.Id, delay, ct);
        }
        return true;
    }
}
