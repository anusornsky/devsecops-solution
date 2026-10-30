// exception ประจำงานสต็อก — ชั้น Api แปลงแต่ละชนิดเป็น HTTP status (ดู Api/ErrorHandling.cs)
// เทียบ Go: ตัวแปร ErrInvalid / ErrNotFound / ... ใน go/internal/domain/product.go
// Go ใช้ "ค่า error" แล้วเช็คด้วย errors.Is · C# ใช้ "ชนิดของ exception" แล้วเช็คด้วย switch/catch ตามชนิด
namespace StockApi.Domain;

// abstract = สร้างตรงๆ ไม่ได้ ต้องสร้างผ่านคลาสลูก · ใช้เป็น "หมวด" ของ exception ทั้งหมดในงานนี้
// (string message) : Exception(message) = รับข้อความแล้วส่งต่อให้คลาสแม่ Exception เก็บไว้ใน .Message
public abstract class StockException(string message) : Exception(message);

public sealed class InvalidProductException(string detail) : StockException($"ข้อมูลไม่ถูกต้อง: {detail}");

// string? detail = null = parameter ไม่บังคับ · ใส่มา = ข้อความยาวขึ้นบอกว่าตัวไหน (Go: fmt.Errorf("%w: %s", ErrNotFound, sku))
// ข้อความต้องตรงกับฝั่ง Go ทุกตัวอักษร เพราะไปอยู่ใน reason ของ order ที่ลูกค้าเห็น
public sealed class ProductNotFoundException(string? detail = null)
    : StockException(detail is null ? "ไม่พบสินค้า" : $"ไม่พบสินค้า: {detail}");

public sealed class ProductAlreadyExistsException() : StockException("มีสินค้านี้แล้ว");

public sealed class InsufficientStockException(string? detail = null)
    : StockException(detail is null ? "สต็อกไม่พอ" : $"สต็อกไม่พอ: {detail}");

public sealed class OrderNotFoundException() : StockException("ไม่พบคำสั่งซื้อ");

// ราคา × จำนวน เกินที่ long เก็บได้ · ไม่ตรวจจะวนเป็นเลขติดลบเงียบๆ (overflow) · Go: ErrTotalTooLarge
public sealed class OrderTotalTooLargeException() : StockException("ยอดรวมเกินที่ระบบรองรับ");
