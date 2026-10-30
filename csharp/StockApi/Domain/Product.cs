// Domain = กฎของงานสต็อกล้วนๆ — ไม่รู้จัก HTTP · SQL · ไฟล์
// เทียบ Go: go/internal/domain/product.go
using System.Text.RegularExpressions;

namespace StockApi.Domain;

// ── พื้นฐาน ──────────────────────────────────────────────────────────────
// record      = class ที่เอาไว้ "ถือข้อมูล" · เทียบค่ากันด้วยเนื้อหา (ไม่ใช่ที่อยู่ในหน่วยความจำ)
//               และค่าเปลี่ยนไม่ได้หลังสร้าง (immutable) — Go ใช้ struct ธรรมดา
// sealed      = ห้ามคลาสอื่นสืบทอดต่อ
// partial     = ให้ compiler เติมโค้ดส่วนที่เหลือให้ได้ (ใช้คู่กับ [GeneratedRegex] ข้างล่าง)
// (string Sku, string Name, int Qty) = "primary constructor" → ได้ property 3 ตัวพร้อม constructor ในบรรทัดเดียว
// ─────────────────────────────────────────────────────────────────────────
// Price = ราคาต่อชิ้น หน่วย "สตางค์" (1500 = 15.00 บาท) · จำนวนเต็มเสมอ — double บวกเงินแล้วเพี้ยน
// (C# มี decimal ที่ไม่เพี้ยน แต่เลือก long ให้ตรงกับ Go ที่ไม่มี decimal ในตัว และ SQLite ก็ไม่มีชนิด decimal จริง)
public sealed partial record Product(string Sku, string Name, int Qty, long Price)
{
    // [GeneratedRegex] = compiler แปลง regex เป็นโค้ด C# ตอน build (เร็วกว่าสร้าง new Regex ตอนรัน)
    // Go: regexp.MustCompile(...) ระดับ package
    [GeneratedRegex("^[A-Z0-9-]{3,20}$")]
    private static partial Regex SkuPattern();

    // internal = ใช้ได้ทั้งโปรเจกต์ แต่ข้างนอกโปรเจกต์มองไม่เห็น · Order ใช้กฎ SKU เดียวกัน (Go ใช้ตัวแปร skuPattern ร่วมกันใน package)
    internal static bool IsValidSku(string sku) => SkuPattern().IsMatch(sku);

    // Create = ทางเดียวที่สร้างสินค้าใหม่ได้ → สินค้าผิดกฎหลุดเข้าระบบไม่ได้
    // static = เรียกจากชื่อคลาสได้เลย Product.Create(...) ไม่ต้องมี object ก่อน
    // string? = อาจเป็น null ได้ (JSON ที่ไม่ส่งช่องนั้นมา) — ?? "" แปลง null เป็นข้อความว่าง
    // ผิดกฎ = throw exception · Go คืน error เป็นค่าที่ 2 แทน (ภาษา Go ไม่มี exception)
    public static Product Create(string? sku, string? name, int qty, long price)
    {
        var s = (sku ?? "").Trim().ToUpperInvariant(); // var = ให้ compiler เดาชนิดเอง (ตรงนี้คือ string)
        var n = (name ?? "").Trim();
        if (!IsValidSku(s))
            throw new InvalidProductException($"sku \"{s}\" ต้องเป็น A-Z 0-9 - ยาว 3-20 ตัว"); // $"..{x}.." = แทรกค่าตัวแปรลงข้อความ
        if (n.Length == 0)
            throw new InvalidProductException("ต้องมีชื่อสินค้า");
        if (qty < 0)
            throw new InvalidProductException($"จำนวนเริ่มต้นติดลบไม่ได้ ({qty})");
        if (price < 0)
            throw new InvalidProductException($"ราคาติดลบไม่ได้ ({price})");
        return new Product(s, n, qty, price);
    }

    // ValidateMove ตรวจจำนวนที่รับเข้า/เบิกออก ต้อง > 0
    // ส่วน "เบิกเกินที่มี" ไม่ตรวจที่นี่ — ต้องรู้ยอดล่าสุดในฐานข้อมูล ณ ตอนเบิก (ดู EfProductRepository.IssueAsync)
    public static void ValidateMove(int qty)
    {
        if (qty <= 0)
            throw new InvalidProductException($"จำนวนต้องมากกว่า 0 ({qty})");
    }
}
