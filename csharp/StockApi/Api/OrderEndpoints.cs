// เส้นทาง /orders — เทียบ Go: ส่วน "POST /orders" / "GET /orders/{id}" ใน go/internal/httpapi/handler.go
using System.Globalization;
using StockApi.Application;
using StockApi.Domain;

namespace StockApi.Api;

public sealed record PlaceOrderRequest(List<OrderItemInput>? Items);

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/orders");

        // 202 Accepted = "รับเรื่องแล้ว แต่ยังทำไม่เสร็จ" (ต่างจาก 201 Created ที่ทำเสร็จแล้ว) · ถามสถานะต่อที่ Location
        orders.MapPost("/", async (PlaceOrderRequest req, OrderService svc, CancellationToken ct) =>
        {
            var o = await svc.PlaceAsync(req.Items, ct);
            return Results.Accepted($"/orders/{o.Id}", o);
        });

        // รับ id เป็น string แล้ว TryParse เอง (เหมือน Go ParseInt) · ไม่ใช้ {id:long} เพราะไม่ match → ASP.NET ตอบ 404 body ว่าง ไม่ตรงกับ Go
        // throw ใน ?: = throw expression · ErrorHandling แปลงเป็น 404 + {"error":"ไม่พบคำสั่งซื้อ"} ให้
        // AllowLeadingSign = ยอมแค่ +/- นำหน้า เหมือน Go ParseInt · TryParse แบบปกติยอมช่องว่างด้วย (" 1" → 1)
        orders.MapGet("/{id}", async (string id, OrderService svc, CancellationToken ct) =>
            long.TryParse(id, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
                ? Results.Ok(await svc.GetAsync(n, ct))
                : throw new OrderNotFoundException());
    }
}
