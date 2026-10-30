// เทียบ Go: go/internal/domain/product_test.go
using StockApi.Domain;

namespace StockApi.Tests.Domain;

// ── พื้นฐาน xUnit ────────────────────────────────────────────────────────
// [Fact]      = เทสต์ 1 กรณี
// [Theory] + [InlineData(...)] = เทสต์เดียวรันหลายชุดข้อมูล · Go: table-driven (slice ของ struct + t.Run)
// Assert.Throws<T>(() => ...) = ต้อง throw ชนิด T ไม่งั้นเทสต์ไม่ผ่าน
// โครง AAA: Arrange (เตรียม) → Act (เรียกของที่ทดสอบ) → Assert (เช็คผล)
// ─────────────────────────────────────────────────────────────────────────
public sealed class ProductTests
{
    [Theory]
    [InlineData("PEN-001", "ปากกา", 10, 1500, false)] // ถูกต้อง
    [InlineData("pen-002", "ปากกา", 0, 0, false)]  // sku ตัวเล็กถูกแปลงเป็นใหญ่
    [InlineData("AB", "ปากกา", 1, 0, true)]        // sku สั้นไป
    [InlineData("PEN 01", "ปากกา", 1, 0, true)]    // sku มีอักขระแปลก
    [InlineData("PEN-003", "   ", 1, 0, true)]     // ไม่มีชื่อ
    [InlineData("PEN-004", "ปากกา", -1, 0, true)]  // จำนวนติดลบ
    [InlineData("PEN-005", "ปากกา", 1, -1, true)]  // ราคาติดลบ
    public void NewProductValidatesEveryRule(string sku, string name, int qty, long price, bool wantError)
    {
        // Act — Record.Exception = รันแล้วเก็บ exception ไว้ (ไม่มี = null) แทนการปล่อยให้เทสต์พัง
        var ex = Record.Exception(() => Product.Create(sku, name, qty, price));

        // Assert
        if (wantError)
            Assert.IsType<InvalidProductException>(ex);
        else
            Assert.Null(ex);
    }

    [Fact]
    public void NewProductNormalisesSku()
    {
        var p = Product.Create("  pen-002 ", "ปากกา", 0, 0);

        Assert.Equal("PEN-002", p.Sku);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateMoveRejectsZeroAndNegative(int qty)
    {
        Assert.Throws<InvalidProductException>(() => Product.ValidateMove(qty));
    }

    [Fact]
    public void ValidateMoveAcceptsPositive()
    {
        Product.ValidateMove(1); // ไม่ throw = ผ่าน
    }
}
