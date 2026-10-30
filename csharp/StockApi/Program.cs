// composition root: อ่าน config → ลงทะเบียนชิ้นส่วนทุกชั้นใน DI → ตั้ง pipeline → เปิด server
// เทียบ Go: go/cmd/stockapi/main.go
//
// ไฟล์นี้ไม่มี class/Main ให้เห็น = "top-level statements" · compiler สร้าง Main ครอบให้เอง
// อ่านเป็น 2 ช่วง: (1) builder = เตรียมของ  (2) app = ต่อด่าน request แล้วรัน
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StockApi.Api;
using StockApi.Application;
using StockApi.Infrastructure;
using StockApi.Worker;

var builder = WebApplication.CreateBuilder(args);

// ── (1) log: JSON ทีละบรรทัด = ระบบเก็บ log (CloudWatch ฯลฯ) อ่านต่อได้เลย · Go: slog.NewJSONHandler
Console.OutputEncoding = Encoding.UTF8; // Windows ตอนส่ง stdout ลงไฟล์จะใช้ code page ไทย (cp874) → log ภาษาไทยเพี้ยน · Linux เป็น UTF-8 อยู่แล้ว
builder.Logging.ClearProviders().AddJsonConsole(o =>
    o.JsonWriterOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Thai) });
// ASP.NET log ทุก exception ที่ UseExceptionHandler จับได้เป็น Error — รวม 404/409 ที่เป็นเรื่องปกติของงาน
// ปิดตัวนี้ แล้วให้ Api/ErrorHandling.cs log เองเฉพาะ 500 (เหมือน Go) · ไม่งั้น alert ดังทั้งคืนทั้งที่ระบบไม่ได้พัง
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);

// ── config: อ่านจาก env DB_PATH (ไม่มี = stock.db) · พอร์ตใช้ของ ASP.NET เอง คือ env ASPNETCORE_URLS
//    (Go อ่าน env ADDR เอง เพราะ Go ไม่มีระบบ config สำเร็จรูป)
// ── DI: บอก container ว่า "ถ้ามีใครขอ X ให้สร้างแบบนี้"
//    AddDbContext / AddScoped = สร้างใหม่ 1 ตัวต่อ 1 request แล้วทิ้ง (DbContext ห้ามใช้ร่วมกันข้าม request)
//    อ่าน DB_PATH ตอนสร้าง (sp = service provider) ไม่ใช่ตอนนี้ → เทสต์เปลี่ยนไฟล์ DB ได้ทีหลัง
//    ConfigureWarnings: SKU ซ้ำ = SQL ล้มตามที่ตั้งใจ (เราแปลงเป็น 409) แต่ EF log เป็น Error ไปก่อนแล้ว → ลดเป็น Debug
//    ถ้าฐานข้อมูลพังจริง exception ยังวิ่งไปถึง ErrorHandling แล้วถูก log เป็น 500 พร้อมรายละเอียดครบ ไม่หายไปไหน
builder.Services.AddDbContext<StockDbContext>((sp, o) => o
    .UseSqlite($"Data Source={sp.GetRequiredService<IConfiguration>()["DB_PATH"] ?? "stock.db"}")
    .ConfigureWarnings(w => w.Log(
        (RelationalEventId.CommandError, LogLevel.Debug),
        (CoreEventId.SaveChangesFailed, LogLevel.Debug))));
builder.Services.AddScoped<IProductRepository, EfProductRepository>(); // ขอ interface → ได้ตัวจริงที่ใช้ EF
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<OrderService>();
// AddHostedService = ให้ระบบเปิด worker ตอน app.Run() และรอให้ปิดเรียบร้อยตอนสั่งหยุด (Go: go worker.Run + รอ channel เอง)
builder.Services.AddHostedService<OrderWorker>();

// ── JSON: ส่งช่องที่ไม่รู้จักมา = 400 (จับพิมพ์ชื่อช่องผิดได้เร็ว) · Go: dec.DisallowUnknownFields()
// + ส่งภาษาไทยออกไปตรงๆ (ค่าเริ่มต้นแปลงเป็น ป... อ่านไม่ออก) · ยัง escape < > & ไว้เหมือนเดิมเพื่อความปลอดภัย
builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Thai);
});
// อ่าน body ไม่ได้ → ให้ throw ทุก environment (ค่าเดิม: throw เฉพาะ Development) เพื่อให้ ErrorHandling ตอบรูปเดียวกันเสมอ
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

// ── กันคำขอผิดปกติ (Go ตั้งที่ http.Server): body ไม่เกิน 1 MB · header ต้องมาครบใน 5 วินาที
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 1 << 20;
    k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
    k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
});
// graceful shutdown: SIGTERM/Ctrl+C → หยุดรับใหม่ รอของค้างไม่เกิน 10 วินาที · Go เขียนเอง (signal.NotifyContext + Shutdown)
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

// สร้างตารางถ้ายังไม่มี (เหมือน CREATE TABLE IF NOT EXISTS ของ Go)
// ponytail: EnsureCreated แทน migration — พอสำหรับตารางเดียว · ตารางเริ่มเปลี่ยนรูปเมื่อไหร่ค่อยใช้ dotnet-ef migrations
// ⚠️ EnsureCreated ใช้ร่วมกับ migration ไม่ได้ — วันที่เปลี่ยนต้องลบบรรทัดนี้ทิ้ง
using (var scope = app.Services.CreateScope()) // using = เรียก Dispose ให้อัตโนมัติเมื่อจบบล็อก (ปิด DbContext)
{
    scope.ServiceProvider.GetRequiredService<StockDbContext>().Database.EnsureCreated();

    // งานที่ค้าง processing จากรอบก่อน (โปรแกรมตายกลางงาน) → คืนเข้าคิว · ทำก่อน app.Run() = ก่อน worker เริ่ม
    var stuck = scope.ServiceProvider.GetRequiredService<IOrderRepository>().RecoverStuckAsync(CancellationToken.None).GetAwaiter().GetResult();
    if (stuck > 0)
        app.Logger.LogWarning("คืนงานค้างเข้าคิว {Orders}", stuck);
}

// ── (2) pipeline: ลำดับมีผล — ตัวจับ error ต้องอยู่หน้าสุด ถึงจะห่อ endpoint ทุกตัวได้
app.UseStockErrorHandling();
app.MapProductEndpoints();
app.MapOrderEndpoints();

app.Run(); // บล็อกจนกว่าจะสั่งปิด

// ให้โปรเจกต์เทสต์อ้างถึง Program ได้ (WebApplicationFactory<Program>) — top-level statements สร้าง Program เป็น internal
public partial class Program;
