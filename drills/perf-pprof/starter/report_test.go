// ตัวตรวจของโจทย์ · ห้ามแก้ไฟล์นี้ · รัน: go test -v ./...
package report

import (
	"fmt"
	"testing"
	"time"
)

// slowReport = โค้ดตั้งต้นฉบับเดิม เก็บไว้เทียบผลและเวลา
func slowReport(products []Product, orders []Order) string {
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

func data(np, no int) ([]Product, []Order) {
	ps := make([]Product, np)
	for i := range ps {
		ps[i] = Product{SKU: fmt.Sprintf("SKU-%d", i), Name: fmt.Sprintf("สินค้า %d", i)}
	}
	ps = append(ps, Product{SKU: "SKU-0", Name: "ชื่อใหม่"}) // SKU ซ้ำ: ตัวหลังชนะ (ต้องได้ผลเหมือนโค้ดเดิม)
	os := make([]Order, no)
	for i := range os {
		os[i] = Order{ID: i, SKU: fmt.Sprintf("SKU-%d", (i*7)%(np+50)), Qty: i%5 + 1} // บางตัวไม่มีสินค้า → "?"
	}
	return ps, os
}

// เกณฑ์ 1: ผลเหมือนเดิมทุกตัวอักษร (รวมกรณีไม่มีสินค้า และ SKU ซ้ำ)
func TestSameOutput(t *testing.T) {
	for _, n := range []int{0, 1, 10, 500} {
		ps, os := data(n+1, n)
		if got, want := Report(ps, os), slowReport(ps, os); got != want {
			t.Fatalf("n=%d ผลไม่เหมือนเดิม", n)
		}
	}
}

// เกณฑ์ 2: เร็วกว่าเดิมอย่างน้อย 2 เท่า (วัดเวลาที่ดีที่สุดจาก 3 รอบ กันเครื่องกระตุก)
func TestFaster(t *testing.T) {
	ps, os := data(3000, 6000)
	best := func(f func([]Product, []Order) string) time.Duration {
		b := time.Duration(1 << 62)
		for i := 0; i < 3; i++ {
			s := time.Now()
			f(ps, os)
			if d := time.Since(s); d < b {
				b = d
			}
		}
		return b
	}
	slow, fast := best(slowReport), best(Report)
	t.Logf("เดิม %v · ใหม่ %v · เร็วขึ้น %.1f เท่า", slow, fast, float64(slow)/float64(fast))
	if fast*2 > slow {
		t.Errorf("ยังเร็วขึ้นไม่ถึง 2 เท่า (เดิม %v ใหม่ %v)", slow, fast)
	}
}

func BenchmarkReport(b *testing.B) {
	ps, os := data(3000, 6000)
	for b.Loop() {
		Report(ps, os)
	}
}
