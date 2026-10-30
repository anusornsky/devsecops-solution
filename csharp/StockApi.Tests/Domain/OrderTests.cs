// เทียบ Go: go/internal/domain/order_test.go
using StockApi.Domain;

namespace StockApi.Tests.Domain;

public sealed class OrderTests
{
    // [MemberData] = เอาชุดข้อมูลจาก property ข้างล่าง (ใช้เมื่อข้อมูลซับซ้อนเกิน InlineData รับได้ เช่น list)
    // TheoryData<T1, T2> = ตารางข้อมูลที่บอกชนิดแต่ละคอลัมน์ · Go: slice ของ struct
    public static TheoryData<string, OrderItemInput[], bool> Cases => new()
    {
        { "ถูกต้อง", [new("PEN-001", 2), new("CUP-001", 1)], false },
        { "ไม่มีสินค้า", [], true },
        { "sku ผิดรูปแบบ", [new("x", 1)], true },
        { "จำนวน 0", [new("PEN-001", 0)], true },
        { "sku ซ้ำ (หลังแปลงตัวใหญ่)", [new("PEN-001", 1), new(" pen-001", 1)], true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void NewOrderValidatesEveryRule(string name, OrderItemInput[] items, bool wantError)
    {
        var ex = Record.Exception(() => Order.Create(items));

        if (wantError)
            Assert.IsType<InvalidProductException>(ex);
        else
            Assert.Null(ex);
        _ = name; // ชื่อกรณีไว้โชว์ในผลเทสต์เท่านั้น
    }

    [Fact]
    public void NewOrderStartsPendingWithNormalisedSkuAndNoPrice()
    {
        var o = Order.Create([new("pen-001", 2)]);

        Assert.Equal(OrderStatus.Pending, o.Status);
        Assert.Equal("PEN-001", o.Items[0].Sku);
        Assert.Equal(0, o.Items[0].UnitPrice); // ราคามาจากระบบตอนตัดสต็อก ไม่ใช่จาก client
    }
}
