// เฉลย · test ที่ตรวจ "ผลลัพธ์ทางธุรกิจ" ไม่ใช่แค่ status code:
//   order ต้องไปถึง done ภายในเวลาที่กำหนด · สต็อกต้องลดตามจำนวน · ยอดรวมต้องถูก
// ของ AI ตรวจแค่ 202 = "รับเรื่องแล้ว" ซึ่งจริงเสมอแม้ worker จะพังทุกครั้ง
// แต่ละ test ใช้ SKU ไม่ซ้ำ (Guid) → รันซ้ำบน DB เดิมได้ ไม่ต้องล้างตาราง
using System.Net;
using System.Text;
using System.Text.Json;
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
    public async Task PlaceOrder_ReachesDone_AndIssuesStock()
    {
        // Arrange
        var sku = "T-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var created = await _client.PostAsync("/products", Json($$"""{"sku":"{{sku}}","name":"ชาเย็น","qty":10,"price":45}"""));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Act
        var placed = await _client.PostAsync("/orders", Json($$"""{"items":[{"sku":"{{sku}}","qty":3}]}"""));
        Assert.Equal(HttpStatusCode.Accepted, placed.StatusCode);
        var id = JsonDocument.Parse(await placed.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt64();
        var order = await WaitFinal(id, TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal("done", order.GetProperty("status").GetString());
        Assert.Equal(135, order.GetProperty("total").GetInt64());
        var stock = JsonDocument.Parse(await _client.GetStringAsync($"/products/{sku}")).RootElement;
        Assert.Equal(7, stock.GetProperty("qty").GetInt32());
    }

    // รอจนสถานะไม่ใช่ pending/processing หรือหมดเวลา · หมดเวลา = คืนสถานะล่าสุด (assert จะบอกว่าค้างที่อะไร)
    private async Task<JsonElement> WaitFinal(long id, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        JsonElement last;
        do
        {
            last = JsonDocument.Parse(await _client.GetStringAsync($"/orders/{id}")).RootElement;
            var s = last.GetProperty("status").GetString();
            if (s is not ("pending" or "processing")) return last;
            await Task.Delay(100);
        } while (DateTime.UtcNow < until);
        return last;
    }

    private static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");
}
