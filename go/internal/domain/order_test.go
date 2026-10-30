package domain

import (
	"errors"
	"testing"
)

func TestNewOrderValidatesEveryRule(t *testing.T) {
	cases := []struct {
		name    string
		items   []OrderItem
		wantErr bool
	}{
		{"ถูกต้อง", []OrderItem{{SKU: "PEN-001", Qty: 2}, {SKU: "CUP-001", Qty: 1}}, false},
		{"ไม่มีสินค้า", nil, true},
		{"sku ผิดรูปแบบ", []OrderItem{{SKU: "x", Qty: 1}}, true},
		{"จำนวน 0", []OrderItem{{SKU: "PEN-001", Qty: 0}}, true},
		{"sku ซ้ำ (หลังแปลงตัวใหญ่)", []OrderItem{{SKU: "PEN-001", Qty: 1}, {SKU: " pen-001", Qty: 1}}, true},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, err := NewOrder(c.items)
			if gotErr := err != nil; gotErr != c.wantErr {
				t.Fatalf("err = %v, wantErr %v", err, c.wantErr)
			}
			if c.wantErr && !errors.Is(err, ErrInvalid) {
				t.Errorf("err = %v, want wraps ErrInvalid", err)
			}
		})
	}
}

// client ส่งราคามาเองต้องถูกทิ้ง — ไม่งั้นใครก็สั่งของราคา 1 สตางค์ได้
func TestNewOrderIgnoresClientPriceAndStartsPending(t *testing.T) {
	o, err := NewOrder([]OrderItem{{SKU: "pen-001", Qty: 2, UnitPrice: 1}})
	if err != nil {
		t.Fatal(err)
	}
	if o.Status != StatusPending || o.Items[0].SKU != "PEN-001" || o.Items[0].UnitPrice != 0 {
		t.Errorf("order = %+v, want pending, PEN-001, unitPrice 0", o)
	}
}
