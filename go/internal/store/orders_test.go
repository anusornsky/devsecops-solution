package store

import (
	"context"
	"errors"
	"math"
	"testing"
	"time"

	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// seed สร้างสินค้า + order 1 ตัว แล้วจองมาให้พร้อม Fulfil
func seed(t *testing.T, s *SQLite, products []domain.Product, items []domain.OrderItem) int64 {
	t.Helper()
	ctx := context.Background()
	for _, p := range products {
		if err := s.Create(ctx, p); err != nil {
			t.Fatal(err)
		}
	}
	o, err := s.CreateOrder(ctx, domain.Order{Status: domain.StatusPending, Items: items})
	if err != nil {
		t.Fatal(err)
	}
	if _, ok, err := s.ClaimNext(ctx); err != nil || !ok {
		t.Fatalf("ClaimNext ok=%v err=%v", ok, err)
	}
	return o.ID
}

func qtyOf(t *testing.T, s *SQLite, sku string) int {
	t.Helper()
	p, err := s.Get(context.Background(), sku)
	if err != nil {
		t.Fatal(err)
	}
	return p.Qty
}

func TestFulfilDeductsStockAndSnapshotsPrice(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	id := seed(t, s,
		[]domain.Product{{SKU: "PEN-001", Name: "ปากกา", Qty: 10, Price: 1500}, {SKU: "CUP-001", Name: "แก้ว", Qty: 5, Price: 5000}},
		[]domain.OrderItem{{SKU: "PEN-001", Qty: 2}, {SKU: "CUP-001", Qty: 1}})

	total, err := s.Fulfil(ctx, id)

	if err != nil {
		t.Fatal(err)
	}
	if total != 2*1500+5000 {
		t.Errorf("total = %d, want 8000", total)
	}
	if qtyOf(t, s, "PEN-001") != 8 || qtyOf(t, s, "CUP-001") != 4 {
		t.Errorf("stock = %d/%d, want 8/4", qtyOf(t, s, "PEN-001"), qtyOf(t, s, "CUP-001"))
	}
	o, _ := s.GetOrder(ctx, id)
	if o.Status != domain.StatusDone || o.Items[1].UnitPrice != 1500 { // เรียงตาม sku: CUP-001, PEN-001
		t.Errorf("order = %+v, want done with PEN-001 unitPrice 1500", o)
	}
}

// เทสต์ที่สำคัญที่สุดของระบบ order: รายการที่ 2 ไม่พอ → รายการที่ 1 ต้องไม่ถูกตัด (transaction rollback)
func TestFulfilIsAllOrNothing(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	id := seed(t, s,
		[]domain.Product{{SKU: "AAA-001", Name: "ตัดได้", Qty: 10}, {SKU: "ZZZ-001", Name: "ไม่พอ", Qty: 1}},
		[]domain.OrderItem{{SKU: "AAA-001", Qty: 3}, {SKU: "ZZZ-001", Qty: 5}}) // AAA ถูกตัดก่อน (เรียงตาม sku)

	_, err := s.Fulfil(ctx, id)

	if !errors.Is(err, domain.ErrInsufficientStock) {
		t.Fatalf("err = %v, want ErrInsufficientStock", err)
	}
	if got := qtyOf(t, s, "AAA-001"); got != 10 {
		t.Errorf("AAA-001 = %d, want 10 (ต้องถูกคืนเพราะ rollback)", got)
	}
	if err.Error() != "สต็อกไม่พอ: ZZZ-001 (มี 1 ต้องการ 5)" {
		t.Errorf("reason = %q", err.Error())
	}
}

// ยอดรวมเกินที่ int64 เก็บได้ → ต้องปฏิเสธ (เดิมวนเป็น -2 แล้วบันทึก done เงียบๆ) และคืนสต็อก
func TestFulfilRejectsTotalOverflow(t *testing.T) {
	s := openTemp(t)
	id := seed(t, s,
		[]domain.Product{{SKU: "GOLD-001", Name: "ทองแท่ง", Qty: 5, Price: math.MaxInt64}},
		[]domain.OrderItem{{SKU: "GOLD-001", Qty: 2}})

	_, err := s.Fulfil(context.Background(), id)

	if !errors.Is(err, domain.ErrTotalTooLarge) {
		t.Fatalf("err = %v, want ErrTotalTooLarge", err)
	}
	if got := qtyOf(t, s, "GOLD-001"); got != 5 {
		t.Errorf("GOLD-001 = %d, want 5 (ต้องถูกคืนเพราะ rollback)", got)
	}
}

func TestFulfilUnknownSkuIsNotFound(t *testing.T) {
	s := openTemp(t)
	id := seed(t, s, nil, []domain.OrderItem{{SKU: "NOPE-1", Qty: 1}})

	_, err := s.Fulfil(context.Background(), id)

	if !errors.Is(err, domain.ErrNotFound) {
		t.Fatalf("err = %v, want ErrNotFound", err)
	}
}

// จองซ้ำไม่ได้: order เดียวต้องถูกหยิบได้ครั้งเดียว
func TestClaimNextTakesEachOrderOnce(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	seed(t, s, nil, []domain.OrderItem{{SKU: "PEN-001", Qty: 1}}) // seed จองไปแล้ว 1 ครั้ง

	_, ok, err := s.ClaimNext(ctx)

	if err != nil || ok {
		t.Errorf("second claim ok=%v err=%v, want ok=false", ok, err)
	}
}

// Requeue พร้อมหน่วงเวลา → ยังหยิบไม่ได้จนกว่าจะถึงเวลา
func TestRequeuedOrderWaitsForDelay(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	id := seed(t, s, nil, []domain.OrderItem{{SKU: "PEN-001", Qty: 1}})

	if err := s.Requeue(ctx, id, time.Hour); err != nil {
		t.Fatal(err)
	}
	if _, ok, _ := s.ClaimNext(ctx); ok {
		t.Error("claimed before delay, want wait")
	}
	if err := s.Requeue(ctx, id, 0); err == nil { // ไม่ได้ processing แล้ว ต้องไม่ยอม
		t.Error("requeue of pending order succeeded, want error")
	}
}

// โปรแกรมตายตอน processing → เปิดใหม่ต้องได้งานคืนเข้าคิว
func TestRecoverStuckReturnsProcessingToQueue(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	seed(t, s, nil, []domain.OrderItem{{SKU: "PEN-001", Qty: 1}})

	n, err := s.RecoverStuck(ctx)

	if err != nil || n != 1 {
		t.Fatalf("recovered %d err=%v, want 1", n, err)
	}
	if _, ok, _ := s.ClaimNext(ctx); !ok {
		t.Error("recovered order not claimable")
	}
}
