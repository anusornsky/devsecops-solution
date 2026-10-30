// บทที่ 1 — พื้นฐาน C# เทียบ Go   รัน: dotnet run --project 01-basics/cs   (จากโฟลเดอร์ lessons)
// เปิดคู่กับ ../go/main.go — แต่ละหัวข้อเลขตรงกัน
// ไฟล์นี้ไม่มี class Program / static void Main ให้เห็น = "top-level statements" compiler เติมให้เอง

Console.WriteLine("── 1. ตัวแปร");
string name = "ปากกา"; // แบบเต็ม: ชนิด ชื่อ = ค่า (Go กลับกัน: ชื่อก่อน ชนิดหลัง)
var qty = 10;          // var = เดาชนิดเอง (Go: qty := 10)
int empty = default;   // default = ค่าศูนย์ของชนิดนั้น · ตัวแปรใน method ต้องใส่ค่าก่อนใช้ (Go ให้ค่าศูนย์เอง)
Console.WriteLine($"{name} {qty} {empty}"); // $"..{x}.." = แทรกค่าในข้อความ

Console.WriteLine("── 2. if / for");
for (var i = 1; i <= 3; i++) // C# ต้องมีวงเล็บรอบเงื่อนไข · Go ไม่มี
{
    if (i % 2 == 0)
        Console.WriteLine($"{i} คู่");
    else
        Console.WriteLine($"{i} คี่");
}

Console.WriteLine("── 3. ผิดพลาด = throw (Go คืน error เป็นค่าที่ 2)");
Console.WriteLine(Divide(10, 2));
try
{
    Console.WriteLine(Divide(10, 0));
}
catch (DivideByZeroException ex) // Go: if err != nil { ... }
{
    Console.WriteLine($"พัง: {ex.Message}");
}

Console.WriteLine("── 4. record + method");
var p = new Product("PEN-001", "ปากกา", 5);
Console.WriteLine(p.Label());
Console.WriteLine(p); // record พิมพ์ทุกช่องให้เอง (Go: fmt.Printf("%+v", p))

Console.WriteLine("── 5. List<T> (Go: slice)");
var items = new List<string> { "a", "b" };
items.Add("c"); // แก้ list ตัวเดิม (Go: append คืนตัวใหม่ ต้องเก็บทับ)
Console.WriteLine($"{string.Join(",", items)} {items.Count}");
foreach (var (s, i) in items.Select((s, i) => (s, i))) // Go: for i, s := range items
    Console.WriteLine($"{i} {s}");

Console.WriteLine("── 6. Dictionary (Go: map)");
var stock = new Dictionary<string, int> { ["PEN-001"] = 5 };
stock["CUP-002"] = 3;
var ok = stock.TryGetValue("NOPE", out var n); // ใช้ stock["NOPE"] ตรงๆ = throw KeyNotFoundException (Go ได้ค่าศูนย์)
Console.WriteLine($"{stock.Count} รายการ {n} {ok}");

Console.WriteLine("── 7. ส่งสำเนา vs ส่งตัวจริง");
// record (class) ส่ง "ตัวจริง" ไป แต่แก้ค่าไม่ได้ (immutable) → ต้องสร้างตัวใหม่ด้วย with
var p2 = AddStock(p, 100);
Console.WriteLine($"{p.Qty} → {p2.Qty}"); // ตัวเดิม 5 · ตัวใหม่ 105
Console.WriteLine(new string('=', 20));

// ── ฟังก์ชันท้ายไฟล์ (top-level statements ต้องอยู่บน · ประกาศ type/ฟังก์ชันไว้ล่าง) ──
static int Divide(int a, int b) =>
    b == 0 ? throw new DivideByZeroException("หารด้วยศูนย์ไม่ได้") : a / b;

static Product AddStock(Product p, int n) => p with { Qty = p.Qty + n }; // with = สำเนาที่เปลี่ยนบางช่อง

// record = class ถือข้อมูล · public ต้องเขียนเอง (Go ใช้ตัวพิมพ์ใหญ่แทน)
public sealed record Product(string Sku, string Name, int Qty)
{
    public string Label() => $"{Name} ({Sku}) เหลือ {Qty}";
}
