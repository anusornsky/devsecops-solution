// โจทย์บันได Performance ขั้น ③ · โค้ดตั้งต้น = ร่างของ AI ที่ผลถูก แต่ช้ามากเมื่อข้อมูลเยอะ
// งานของคุณ:
//   1. หาว่าช้าตรงไหนด้วย pprof (ไม่ใช่เดา):  go test -bench . -benchmem -cpuprofile cpu.out
//                                             go tool pprof -top cpu.out
//   2. แก้ Report ให้ go test ./... ผ่าน = ผลเหมือนเดิมทุกตัวอักษร และเร็วกว่าเดิมอย่างน้อย 2 เท่า
//   3. จดตัวเลข ns/op ก่อน-หลัง ไว้ในข้อความ commit
package report

import "fmt"

type Product struct {
	SKU  string
	Name string
}

type Order struct {
	ID  int
	SKU string
	Qty int
}

// Report คืนรายงาน 1 บรรทัดต่อ order: "#<id> <ชื่อสินค้า> x<qty>"
func Report(products []Product, orders []Order) string {
	out := ""
	for _, o := range orders {
		name := "?"
		for _, p := range products {
			if p.SKU == o.SKU {
				name = p.Name
			}
		}
		out += fmt.Sprintf("#%d %s x%d\n", o.ID, name, o.Qty)
	}
	return out
}
