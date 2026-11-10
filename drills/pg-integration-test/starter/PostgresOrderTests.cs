// โจทย์บันได Test ขั้น ③ · test ที่ AI เขียนให้ "ครอบ Postgres แล้ว" — ผ่านเขียวทั้งที่ระบบมี bug
// เรื่องจริงจาก P3: เปิด EnableRetryOnFailure แล้ว order ค้าง pending ตลอด (retry ไม่ยอมให้เปิด transaction เอง) · test 55 ตัวเดิมใช้ SQLite เลยไม่มีใครเห็น
// งานของคุณ: แก้ test นี้ให้ pwsh -File ..\check.ps1 starter ผ่าน =
//   รันกับโค้ดที่แก้แล้ว → เขียว · รันกับโค้ดที่ใส่ bug กลับเข้าไป → แดง
// ไฟล์นี้ถูกก๊อปไปวางใน csharp/StockApi.Tests/ ตอนตรวจ · ต่อ Postgres ผ่าน env PG_TEST (ตัวตรวจตั้งให้)
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using StockApi.Worker;

namespace StockApi.Tests.Integration;

[Trait("Category", "Postgres")]
public sealed class PostgresOrderTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PostgresOrderTests()
    {
        var pg = Environment.GetEnvironmentVariable("PG_TEST") ?? throw new InvalidOperationException("ตั้ง env PG_TEST ก่อน");
        OrderWorker.Interval = TimeSpan.FromMilliseconds(50);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("DB_CONNECTION", pg));
        _client = _factory.CreateClient();
    }

    public void Dispose() { _client.Dispose(); _factory.Dispose(); }

    [Fact]
    public async Task PlaceOrder_Works()
    {
        await _client.PostAsync("/products", Json("""{"sku":"TEA-1","name":"ชาเย็น","qty":10,"price":45}"""));
        var res = await _client.PostAsync("/orders", Json("""{"items":[{"sku":"TEA-1","qty":3}]}"""));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
    }

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");
}
