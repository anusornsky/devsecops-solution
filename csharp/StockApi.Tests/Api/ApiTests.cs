// เทียบ Go: go/internal/httpapi/handler_test.go
// ยิง HTTP เข้า API จริงทุกชั้น (Program.cs ทั้งไฟล์) + SQLite ไฟล์ชั่วคราว — ไม่ต้องเปิด port จริง
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using StockApi.Worker;

namespace StockApi.Tests.Api;

public sealed class ApiTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"stock-api-{Guid.NewGuid()}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    // WebApplicationFactory<Program> = รัน Program.cs ทั้งหมดในหน่วยความจำ + ให้ HttpClient ที่ยิงเข้าไปตรงๆ
    // UseSetting("DB_PATH", ...) = แทนค่า config → Program.cs อ่าน DB_PATH ได้ไฟล์ชั่วคราวของเทสต์นี้
    // Go: httptest.NewServer(New(...))
    public ApiTests()
    {
        OrderWorker.Interval = TimeSpan.FromMilliseconds(20); // worker พักสั้นๆ ให้เทสต์ order เร็ว (โปรแกรมจริง 500ms)
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("DB_PATH", _path));
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    // Call ยิง request แล้วคืน (status, body) — tuple = คืนหลายค่าพร้อมกัน (Go คืนหลายค่าได้อยู่แล้ว)
    private async Task<(HttpStatusCode Status, string Body)> Call(string method, string path, string? body = null)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await _client.SendAsync(req);
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    // เส้นทางหลักของงานจริง: สร้าง → รับเข้า → เบิก → เบิกเกิน → ดูยอด
    [Fact]
    public async Task StockLifecycle()
    {
        var steps = new (string Method, string Path, string? Body, HttpStatusCode Want, string BodyHas)[]
        {
            ("POST", "/products", """{"sku":"PEN-001","name":"ปากกา","qty":5}""", HttpStatusCode.Created, "\"qty\":5"),
            ("POST", "/products", """{"sku":"PEN-001","name":"ซ้ำ","qty":1}""", HttpStatusCode.Conflict, "มีสินค้านี้แล้ว"),
            ("POST", "/products/PEN-001/receive", """{"qty":10}""", HttpStatusCode.OK, "\"qty\":15"),
            ("POST", "/products/PEN-001/issue", """{"qty":12}""", HttpStatusCode.OK, "\"qty\":3"),
            ("POST", "/products/PEN-001/issue", """{"qty":4}""", HttpStatusCode.Conflict, "สต็อกไม่พอ"),
            ("GET", "/products/PEN-001", null, HttpStatusCode.OK, "\"qty\":3"),
            ("GET", "/products/NOPE-1", null, HttpStatusCode.NotFound, "ไม่พบสินค้า"),
        };
        // """ ... """ = raw string literal: เขียน " ข้างในได้โดยไม่ต้อง escape

        foreach (var s in steps)
        {
            var (status, body) = await Call(s.Method, s.Path, s.Body);
            Assert.True(status == s.Want && body.Contains(s.BodyHas),
                $"{s.Method} {s.Path} → {(int)status} {body}; want {(int)s.Want} containing {s.BodyHas}");
        }
    }

    [Theory]
    [InlineData("""{"sku":""")]                                       // JSON พัง
    [InlineData("""{"sku":"PEN-001","name":"x","qty":1,"color":"red"}""")] // ช่องที่ไม่รู้จัก
    [InlineData("""{"sku":"a","name":"x","qty":1}""")]                 // sku ผิดรูปแบบ
    [InlineData("""{"sku":"PEN-002","name":"x","qty":-1}""")]          // จำนวนเริ่มติดลบ
    public async Task BadInputIs400(string body)
    {
        var (status, got) = await Call("POST", "/products", body);

        Assert.True(status == HttpStatusCode.BadRequest, $"status {(int)status} ({got}), want 400");
        Assert.Contains("error", got);
    }

    [Fact]
    public async Task ListEmptyIsJsonArray()
    {
        var (status, body) = await Call("GET", "/products");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("[]", body);
    }

    [Fact]
    public async Task Healthz()
    {
        var (status, body) = await Call("GET", "/healthz");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("ok", body);
    }

    [Fact]
    public async Task WrongMethodIs405()
    {
        var (status, _) = await Call("DELETE", "/products/PEN-001");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
    }

    // ── order: worker จริงวิ่งอยู่ใน factory (AddHostedService) ──────────────

    // WaitOrder ถาม GET /orders/{id} ซ้ำจนสถานะไม่ใช่ pending/processing (ไม่เกิน 3 วินาที)
    // งาน async ทดสอบแบบนี้: ไม่ sleep เวลาตายตัว แต่ถามจนได้ผล หรือหมดเวลา
    private async Task<JsonElement> WaitOrder(long id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var (_, body) = await Call("GET", $"/orders/{id}");
            var o = JsonDocument.Parse(body).RootElement; // อ่าน JSON แบบไม่ต้องสร้าง class (Go: map[string]any)
            if (o.GetProperty("status").GetString() is not ("pending" or "processing"))
                return o;
            await Task.Delay(20);
        }
        throw new TimeoutException("order ไม่เสร็จภายใน 3 วินาที");
    }

    // PlaceOrder ยิง POST /orders ต้องได้ 202 + pending แล้วคืน id
    private async Task<long> PlaceOrder(string body)
    {
        var (status, got) = await Call("POST", "/orders", body);
        Assert.True(status == HttpStatusCode.Accepted, $"POST /orders → {(int)status} {got}, want 202");
        var o = JsonDocument.Parse(got).RootElement;
        Assert.Equal("pending", o.GetProperty("status").GetString());
        return o.GetProperty("id").GetInt64();
    }

    // เส้นทางหลักของ order: สั่ง → 202 pending → worker ทำ → done + ยอดเงิน + สต็อกลด
    [Fact]
    public async Task OrderLifecycle()
    {
        await Call("POST", "/products", """{"sku":"PEN-001","name":"ปากกา","qty":5,"price":1500}""");

        var o = await WaitOrder(await PlaceOrder("""{"items":[{"sku":"PEN-001","qty":2}]}"""));

        Assert.Equal("done", o.GetProperty("status").GetString());
        Assert.Equal(3000, o.GetProperty("total").GetInt64());
        Assert.Contains("\"qty\":3", (await Call("GET", "/products/PEN-001")).Body);
    }

    // สต็อกไม่พอ: POST ยังได้ 202 (ตอนรับเรื่องยังไม่รู้) → worker ตีเป็น failed พร้อมเหตุผล
    [Fact]
    public async Task OrderFailsWhenStockShort()
    {
        await Call("POST", "/products", """{"sku":"PEN-001","name":"ปากกา","qty":1,"price":1500}""");

        var o = await WaitOrder(await PlaceOrder("""{"items":[{"sku":"PEN-001","qty":2}]}"""));

        Assert.Equal("failed", o.GetProperty("status").GetString());
        Assert.Equal("สต็อกไม่พอ: PEN-001 (มี 1 ต้องการ 2)", o.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("POST", "/orders", """{"items":[]}""", 400)]
    [InlineData("POST", "/orders", """{"items":[{"sku":"PEN-001","qty":0}]}""", 400)]
    [InlineData("GET", "/orders/999", null, 404)]
    [InlineData("GET", "/orders/abc", null, 404)]
    public async Task OrderBadInputAndNotFound(string method, string path, string? body, int want)
    {
        var (status, got) = await Call(method, path, body);

        Assert.True((int)status == want, $"{method} {path} → {(int)status} {got}, want {want}");
    }

    // id ที่ไม่ใช่ตัวเลขต้องได้ body error ภาษาไทยเหมือน Go — เคสบน OrderBadInputAndNotFound เช็คแค่ status จึงไม่เห็นว่า body ว่าง
    [Fact]
    public async Task OrderIdNotNumberReturnsThaiError()
    {
        // Arrange: มี order id 1 จริง → " 1" (%20 = ช่องว่าง) ต้องไม่ถูกอ่านเป็น 1 เพราะ Go ParseInt ไม่ยอมช่องว่าง
        await Call("POST", "/orders", """{"items":[{"sku":"PEN-001","qty":1}]}""");

        foreach (var path in new[] { "/orders/abc", "/orders/%201" })
        {
            // Act
            var (status, body) = await Call("GET", path);

            // Assert
            Assert.True((int)status == 404 && body.Contains("ไม่พบคำสั่งซื้อ"), $"GET {path} → {(int)status} {body}, want 404 + ไม่พบคำสั่งซื้อ");
        }
    }
}
