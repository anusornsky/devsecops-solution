// คำสั่งซื้อ — กฎล้วนๆ ไม่รู้จัก HTTP / SQL · เทียบ C#: StockApi/Domain/Order.cs
package domain

import (
	"errors"
	"fmt"
	"strings"
)

var ErrOrderNotFound = errors.New("ไม่พบคำสั่งซื้อ")

// ErrTotalTooLarge = ราคา × จำนวน เกินที่ int64 เก็บได้ · ไม่ตรวจจะวนเป็นเลขติดลบเงียบๆ (เรียกว่า overflow)
var ErrTotalTooLarge = errors.New("ยอดรวมเกินที่ระบบรองรับ")

// Status = สถานะของ order · type ใหม่ที่ข้างในเป็น string (C#: const string ในคลาส OrderStatus)
// เส้นทางที่เป็นไปได้:
//   - pending → processing → done
//   - pending → processing → failed  (สินค้าไม่มี / ไม่พอ / ลองครบแล้ว)
//   - pending → processing → pending (พังชั่วคราว รอลองใหม่)
type Status string

const (
	StatusPending    Status = "pending"    // รอ worker หยิบ
	StatusProcessing Status = "processing" // worker กำลังทำ
	StatusDone       Status = "done"       // ตัดสต็อกแล้ว คิดเงินแล้ว
	StatusFailed     Status = "failed"     // ทำไม่ได้ ดู Reason
)

// OrderItem = สินค้า 1 บรรทัดใน order · UnitPrice เป็น 0 จนกว่า worker จะตัดสต็อก (จำราคา ณ ตอนนั้น)
type OrderItem struct {
	SKU       string `json:"sku"`
	Qty       int    `json:"qty"`
	UnitPrice int64  `json:"unitPrice"`
}

type Order struct {
	ID       int64       `json:"id"`
	Status   Status      `json:"status"`
	Items    []OrderItem `json:"items"`
	Total    int64       `json:"total"`  // สตางค์
	Reason   string      `json:"reason"` // ทำไม failed
	Attempts int         `json:"attempts"`
}

// NewOrder ตรวจกฎแล้วคืน order สถานะ pending — ทางเดียวที่สร้าง order ได้
// ไม่เช็คว่ามี SKU นี้จริงไหม/สต็อกพอไหม — นั่นเป็นงานของ worker ตอนตัดสต็อก (สถานะอาจเปลี่ยนระหว่างรอคิว)
func NewOrder(items []OrderItem) (Order, error) {
	if len(items) == 0 {
		return Order{}, fmt.Errorf("%w: ต้องมีสินค้าอย่างน้อย 1 รายการ", ErrInvalid)
	}
	seen := map[string]bool{} // map ใช้เป็น "เซ็ต" กันซ้ำ (C#: HashSet<string>)
	clean := make([]OrderItem, 0, len(items))
	for _, it := range items {
		sku := strings.ToUpper(strings.TrimSpace(it.SKU))
		if !skuPattern.MatchString(sku) {
			return Order{}, fmt.Errorf("%w: sku %q ต้องเป็น A-Z 0-9 - ยาว 3-20 ตัว", ErrInvalid, sku)
		}
		if it.Qty <= 0 {
			return Order{}, fmt.Errorf("%w: จำนวนของ %s ต้องมากกว่า 0 (%d)", ErrInvalid, sku, it.Qty)
		}
		if seen[sku] {
			return Order{}, fmt.Errorf("%w: sku %s ซ้ำในคำสั่งซื้อเดียวกัน", ErrInvalid, sku)
		}
		seen[sku] = true
		clean = append(clean, OrderItem{SKU: sku, Qty: it.Qty}) // ทิ้ง UnitPrice ที่ client ส่งมา — ราคาต้องมาจากระบบเท่านั้น
	}
	return Order{Status: StatusPending, Items: clean}, nil
}
