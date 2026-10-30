// คำสั่งซื้อ — กฎล้วนๆ ไม่รู้จัก HTTP / SQL · เทียบ Go: go/internal/domain/order.go
using System.Text.Json.Serialization;

namespace StockApi.Domain;

// เส้นทางที่เป็นไปได้:
//   pending → processing → done
//   pending → processing → failed  (สินค้าไม่มี / ไม่พอ / ลองครบแล้ว)
//   pending → processing → pending (พังชั่วคราว รอลองใหม่)
// static class + const = ค่าคงที่รวมไว้ที่เดียว · Go: type Status string + const
// ทางที่ไม่เลือก: enum — ต้องแปลงเป็น string เองทั้งตอนเก็บ DB และตอนส่ง JSON ให้ตรงกับฝั่ง Go
public static class OrderStatus
{
    public const string Pending = "pending";       // รอ worker หยิบ
    public const string Processing = "processing"; // worker กำลังทำ
    public const string Done = "done";             // ตัดสต็อกแล้ว คิดเงินแล้ว
    public const string Failed = "failed";         // ทำไม่ได้ ดู Reason
}

// ── พื้นฐาน: ทำไม Order เป็น class ไม่ใช่ record เหมือน Product ──────────
// Order มี "รายการสินค้า" (List) ข้างใน · EF Core ต้องเติม List นี้เองหลังสร้าง object
// → ใช้ class + { get; private set; } = ข้างนอกอ่านได้ แต่แก้ได้เฉพาะในคลาสนี้ (และ EF ที่แอบเซ็ตผ่าน reflection)
// private Order() {} = constructor ว่างไว้ให้ EF ใช้ตอนอ่านจากฐานข้อมูล · คนอื่นต้องสร้างผ่าน Create เท่านั้น
// ─────────────────────────────────────────────────────────────────────────
public sealed class Order
{
    public long Id { get; private set; }
    public string Status { get; private set; } = OrderStatus.Pending;
    public List<OrderItem> Items { get; private set; } = [];  // [] = collection expression (C# 12) เท่ากับ new List<OrderItem>()
    public long Total { get; private set; }                    // สตางค์
    public string Reason { get; private set; } = "";           // ทำไม failed
    public int Attempts { get; private set; }

    [JsonIgnore] // เวลาที่ worker หยิบได้ (unix ms) — เรื่องภายใน ไม่ส่งให้ client · Go: `json:"-"`
    public long RunAt { get; private set; }

    private Order() { }

    // Create ตรวจกฎแล้วคืน order สถานะ pending
    // ไม่เช็คว่ามี SKU นี้จริงไหม/สต็อกพอไหม — นั่นเป็นงานของ worker ตอนตัดสต็อก (สถานะอาจเปลี่ยนระหว่างรอคิว)
    // IEnumerable<T> = อะไรก็ได้ที่วนได้ (List, array, ผล LINQ) · รับกว้างๆ ไว้ คนเรียกไม่ต้องแปลงก่อน
    public static Order Create(IEnumerable<OrderItemInput>? items)
    {
        var list = items?.ToList() ?? [];
        if (list.Count == 0)
            throw new InvalidProductException("ต้องมีสินค้าอย่างน้อย 1 รายการ");

        var seen = new HashSet<string>(); // HashSet = เซ็ตกันซ้ำ · Add คืน false ถ้ามีแล้ว (Go: map[string]bool)
        var order = new Order();
        foreach (var it in list)
        {
            var sku = (it.Sku ?? "").Trim().ToUpperInvariant();
            if (!Product.IsValidSku(sku))
                throw new InvalidProductException($"sku \"{sku}\" ต้องเป็น A-Z 0-9 - ยาว 3-20 ตัว");
            if (it.Qty <= 0)
                throw new InvalidProductException($"จำนวนของ {sku} ต้องมากกว่า 0 ({it.Qty})");
            if (!seen.Add(sku))
                throw new InvalidProductException($"sku {sku} ซ้ำในคำสั่งซื้อเดียวกัน");
            order.Items.Add(new OrderItem(sku, it.Qty)); // ไม่รับราคาจาก client — ราคาต้องมาจากระบบเท่านั้น
        }
        return order;
    }
}

// OrderItem = สินค้า 1 บรรทัดใน order · UnitPrice เป็น 0 จนกว่า worker จะตัดสต็อก (จำราคา ณ ตอนนั้น)
public sealed class OrderItem(string sku, int qty)
{
    [JsonIgnore] public long OrderId { get; private set; } // FK ไปหา order · EF เติมให้ตอน SaveChanges
    public string Sku { get; private set; } = sku;
    public int Qty { get; private set; } = qty;
    public long UnitPrice { get; private set; }
}

// สิ่งที่ client ส่งมา 1 บรรทัด (แยกจาก OrderItem เพราะ client ไม่มีสิทธิ์กำหนดราคา/OrderId)
public sealed record OrderItemInput(string? Sku, int Qty);
