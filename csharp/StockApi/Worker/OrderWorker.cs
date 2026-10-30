// ตัวทำงานเบื้องหลัง วนหยิบ order จากคิวมาทำจนกว่าจะสั่งปิด
// เทียบ Go: go/internal/worker/worker.go
using StockApi.Application;

namespace StockApi.Worker;

// ── พื้นฐาน: BackgroundService ───────────────────────────────────────────
// คลาสของ ASP.NET สำหรับงานที่วิ่งตลอดอายุโปรแกรม · ลงทะเบียนด้วย AddHostedService<OrderWorker>()
// ระบบเรียก ExecuteAsync ให้เองตอนเปิด · ตอนปิด (Ctrl+C / docker stop) stoppingToken ถูกยกเลิก แล้ว "รอ" ให้ method นี้จบ
// Go ไม่มีของสำเร็จรูป: เขียน go worker.Run(ctx) เอง + รอด้วย channel เอง (ดู main.go)
//
// ⚠️ ทำไมต้อง IServiceScopeFactory แทนการรับ OrderService ตรงๆ:
// worker มีตัวเดียวตลอดอายุโปรแกรม (singleton) แต่ DbContext ต้องสร้างใหม่ทุกงาน (scoped)
// ถ้ารับ OrderService ตรงๆ = ใช้ DbContext ตัวเดียวไปตลอด → ข้อมูลค้างในหน่วยความจำ + ใช้ข้าม thread ไม่ได้
// จึงสร้าง scope ใหม่ทุกงาน = เหมือนทุก order คือ "request" ใหม่ 1 ครั้ง
// ─────────────────────────────────────────────────────────────────────────
public sealed class OrderWorker(IServiceScopeFactory scopes, ILogger<OrderWorker> log) : BackgroundService
{
    // พักระหว่างรอบเมื่อคิวว่าง · 500ms = order ใหม่รอไม่เกินครึ่งวินาที (เหมือน Go)
    // ponytail: ถามซ้ำเป็นรอบ (polling) · ต้องการไวกว่านี้ค่อยเพิ่มสัญญาณ "มีงานใหม่" จาก PlaceAsync มาปลุก
    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("worker started {Interval}", Interval);
        // PeriodicTimer = นาฬิกาที่ await รอรอบถัดไปได้ · Go: time.After ใน select
        using var timer = new PeriodicTimer(Interval);
        var broken = false; // รอบก่อนพัง = อาจมี order ค้าง processing (ย้ายสถานะไม่สำเร็จ) ต้องคืนก่อนทำต่อ
        try
        {
            do
            {
                if (broken)
                    broken = !await RecoverStuckAsync();
                if (!broken)
                    broken = !await DrainAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken)); // สั่งปิด → throw OperationCanceledException
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // ปิดตามปกติ ไม่ใช่ error
        }
        log.LogInformation("worker stopped");
    }

    // ทำงานจนคิวว่าง · เช็ค stoppingToken ระหว่างงาน ไม่ใช่กลางงาน
    // ส่ง CancellationToken.None ให้งาน = งานที่เริ่มแล้วทำจนจบแม้สั่งปิด (ไม่ตัดกลาง transaction) · Go: context.WithoutCancel
    // คืน false = ระบบมีปัญหา
    private async Task<bool> DrainAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var orders = scope.ServiceProvider.GetRequiredService<OrderService>();
            try
            {
                if (!await orders.ProcessNextAsync(CancellationToken.None))
                    return true; // คิวว่าง
            }
            catch (Exception ex)
            {
                log.LogError(ex, "worker"); // ระบบมีปัญหา → พักรอบนี้ รอบหน้าค่อยลองใหม่ (worker ไม่ตาย)
                return false;
            }
        }
        return true;
    }

    // คืน order ที่ค้าง processing เข้าคิว · false = ยังทำไม่ได้ (ฐานข้อมูลยังมีปัญหา) รอบหน้าลองใหม่
    // ปลอดภัยเพราะเรียกระหว่างรอบ = worker ตัวนี้ไม่ได้ถืองานไหนอยู่
    // shortcut: คืนทุก order ที่ processing ได้เพราะมี worker ตัวเดียว, ถ้ารันหลาย instance ต้องเปลี่ยนเป็น lease · Go: recoverStuck
    private async Task<bool> RecoverStuckAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            var n = await scope.ServiceProvider.GetRequiredService<OrderService>().RecoverStuckAsync(CancellationToken.None);
            if (n > 0)
                log.LogWarning("คืนงานค้างเข้าคิว {Orders}", n);
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "worker คืนงานค้างไม่ได้");
            return false;
        }
    }
}
