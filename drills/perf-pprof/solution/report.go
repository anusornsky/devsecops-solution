// เฉลย: pprof ชี้ 2 จุด — (1) หาสินค้าซ้อนลูป O(orders×products) (2) out += ก๊อป string ทั้งก้อนทุกรอบ O(n²)
// แก้: map หาชื่อทีเดียว + strings.Builder ต่อท้ายโดยไม่ก๊อปซ้ำ · ผลเหมือนเดิม: SKU ซ้ำให้ตัวหลังชนะ (เขียนทับใน map ตามลำดับ)
package report

import (
	"fmt"
	"strings"
)

type Product struct {
	SKU  string
	Name string
}

type Order struct {
	ID  int
	SKU string
	Qty int
}

func Report(products []Product, orders []Order) string {
	names := make(map[string]string, len(products))
	for _, p := range products {
		names[p.SKU] = p.Name
	}
	var b strings.Builder
	for _, o := range orders {
		name, ok := names[o.SKU]
		if !ok {
			name = "?"
		}
		fmt.Fprintf(&b, "#%d %s x%d\n", o.ID, name, o.Qty)
	}
	return b.String()
}
