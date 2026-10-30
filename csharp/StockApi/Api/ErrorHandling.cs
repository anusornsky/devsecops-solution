// จุดเดียวที่แปลง exception → HTTP status + {"error": "..."}
// เทียบ Go: func respond(...) ใน go/internal/httpapi/handler.go
using Microsoft.AspNetCore.Diagnostics;
using StockApi.Domain;

namespace StockApi.Api;

public static class ErrorHandling
{
    // UseExceptionHandler = middleware ที่ห่อทุก request ไว้ใน try/catch ใหญ่
    // middleware = ด่านที่ request วิ่งผ่านก่อนถึง endpoint (และวิ่งกลับตอนตอบ) · Go: ฟังก์ชันที่ห่อ http.Handler
    public static void UseStockErrorHandling(this WebApplication app) =>
        app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
        {
            var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error; // ?. = ถ้าทางซ้ายเป็น null ให้ได้ null ไม่พัง

            // switch expression: เลือกผลตามชนิดของ ex · _ = กรณีอื่นทั้งหมด
            var (status, message) = ex switch
            {
                InvalidProductException => (StatusCodes.Status400BadRequest, ex.Message),
                ProductNotFoundException or OrderNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                ProductAlreadyExistsException or InsufficientStockException => (StatusCodes.Status409Conflict, ex.Message),
                // JSON พัง / ช่องที่ไม่รู้จัก — ASP.NET โยนตัวนี้ตอนแปลง body ไม่ได้ · รายละเอียดจริงอยู่ใน InnerException
                BadHttpRequestException bad => (bad.StatusCode, "JSON ไม่ถูกต้อง: " + (bad.InnerException?.Message ?? bad.Message)),
                _ => (StatusCodes.Status500InternalServerError, "internal error"),
            };

            // error ที่ไม่รู้จัก: รายละเอียดเก็บใน log ฝั่งเรา ไม่ส่งให้ client (กันข้อมูลภายในหลุด เช่น path/SQL)
            if (status == StatusCodes.Status500InternalServerError)
                ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("StockApi.Errors").LogError(ex, "unexpected");

            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsJsonAsync(new { error = message });
        }));
}
