// Package domain = กฎของงานสต็อกล้วนๆ — ไม่รู้จัก HTTP · SQL · ไฟล์ (เทียบ C#: โปรเจกต์ StockApi/Domain)
package domain

import (
	"errors"
	"fmt"
	"regexp"
	"strings"
)

// error ประจำตัว (sentinel) — ชั้นอื่นแยกกรณีด้วย errors.Is แล้วแปลงเป็น HTTP status เอง
// C#: exception class เฉพาะ เช่น ProductNotFoundException
var (
	ErrInvalid           = errors.New("ข้อมูลไม่ถูกต้อง")
	ErrNotFound          = errors.New("ไม่พบสินค้า")
	ErrAlreadyExists     = errors.New("มีสินค้านี้แล้ว")
	ErrInsufficientStock = errors.New("สต็อกไม่พอ")
)

// skuPattern = ตัวพิมพ์ใหญ่ ตัวเลข ขีด ยาว 3-20 ตัว เช่น "PEN-001"
var skuPattern = regexp.MustCompile(`^[A-Z0-9-]{3,20}$`)

// Product = สินค้า 1 ตัวในคลัง · Qty เป็นจำนวนคงเหลือ ห้ามติดลบ
type Product struct {
	SKU  string `json:"sku"`
	Name string `json:"name"`
	Qty  int    `json:"qty"`
	// Price = ราคาต่อชิ้น หน่วย "สตางค์" (1500 = 15.00 บาท)
	// เก็บเป็นจำนวนเต็มเสมอ — float บวกเงินแล้วเพี้ยน (0.1 + 0.2 = 0.30000000000000004) · int64 รับยอดได้ถึงหลักล้านล้าน
	Price int64 `json:"price"`
}

// NewProduct ตรวจกฎทุกข้อก่อนคืน Product — ทางเดียวที่สร้างสินค้าใหม่ได้ จึงไม่มีสินค้าผิดกฎหลุดเข้าระบบ
// คืน error ที่ห่อ ErrInvalid ไว้ (%w) ให้ชั้น HTTP ตอบ 400 พร้อมข้อความว่าผิดตรงไหน
func NewProduct(sku, name string, qty int, price int64) (Product, error) {
	sku = strings.ToUpper(strings.TrimSpace(sku))
	name = strings.TrimSpace(name)
	if !skuPattern.MatchString(sku) {
		return Product{}, fmt.Errorf("%w: sku %q ต้องเป็น A-Z 0-9 - ยาว 3-20 ตัว", ErrInvalid, sku)
	}
	if name == "" {
		return Product{}, fmt.Errorf("%w: ต้องมีชื่อสินค้า", ErrInvalid)
	}
	if qty < 0 {
		return Product{}, fmt.Errorf("%w: จำนวนเริ่มต้นติดลบไม่ได้ (%d)", ErrInvalid, qty)
	}
	if price < 0 {
		return Product{}, fmt.Errorf("%w: ราคาติดลบไม่ได้ (%d)", ErrInvalid, price)
	}
	return Product{SKU: sku, Name: name, Qty: qty, Price: price}, nil
}

// ValidateMove ตรวจจำนวนที่รับเข้า/เบิกออก — ต้องมากกว่า 0 (เบิก 0 ชิ้นหรือติดลบไม่มีความหมาย)
// ส่วน "เบิกเกินที่มี" ไม่ตรวจที่นี่ เพราะต้องรู้จำนวนล่าสุดในฐานข้อมูล ณ ตอนเบิก (ดู store.Issue)
func ValidateMove(qty int) error {
	if qty <= 0 {
		return fmt.Errorf("%w: จำนวนต้องมากกว่า 0 (%d)", ErrInvalid, qty)
	}
	return nil
}
