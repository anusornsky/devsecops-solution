package domain

import (
	"errors"
	"testing"
)

// เทสต์แบบตาราง (table-driven) — C#: [Theory] + [InlineData]
func TestNewProductValidatesEveryRule(t *testing.T) {
	cases := []struct {
		name    string
		sku     string
		pname   string
		qty     int
		price   int64
		wantErr bool
	}{
		{"ถูกต้อง", "PEN-001", "ปากกา", 10, 1500, false},
		{"sku ตัวเล็กถูกแปลงเป็นใหญ่", "pen-002", "ปากกา", 0, 0, false},
		{"sku สั้นไป", "AB", "ปากกา", 1, 0, true},
		{"sku มีอักขระแปลก", "PEN 01", "ปากกา", 1, 0, true},
		{"ไม่มีชื่อ", "PEN-003", "   ", 1, 0, true},
		{"จำนวนติดลบ", "PEN-004", "ปากกา", -1, 0, true},
		{"ราคาติดลบ", "PEN-005", "ปากกา", 1, -1, true},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			_, err := NewProduct(c.sku, c.pname, c.qty, c.price)
			if gotErr := err != nil; gotErr != c.wantErr {
				t.Fatalf("err = %v, wantErr %v", err, c.wantErr)
			}
			if c.wantErr && !errors.Is(err, ErrInvalid) {
				t.Errorf("err = %v, want wraps ErrInvalid", err)
			}
		})
	}
}

func TestNewProductNormalisesSKU(t *testing.T) {
	p, err := NewProduct("  pen-002 ", "ปากกา", 0, 0)
	if err != nil {
		t.Fatal(err)
	}
	if p.SKU != "PEN-002" {
		t.Errorf("SKU = %q, want PEN-002", p.SKU)
	}
}

func TestValidateMoveRejectsZeroAndNegative(t *testing.T) {
	for _, qty := range []int{0, -5} {
		if err := ValidateMove(qty); !errors.Is(err, ErrInvalid) {
			t.Errorf("ValidateMove(%d) = %v, want ErrInvalid", qty, err)
		}
	}
	if err := ValidateMove(1); err != nil {
		t.Errorf("ValidateMove(1) = %v, want nil", err)
	}
}
