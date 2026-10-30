package store

import (
	"context"
	"errors"
	"path/filepath"
	"sync"
	"testing"

	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// openTemp = ฐานข้อมูลจริงในไฟล์ชั่วคราว (ลบให้เองตอนเทสต์จบ) — integration test ไม่ใช้ของปลอม
func openTemp(t *testing.T) *SQLite {
	t.Helper()
	s, err := Open(context.Background(), filepath.Join(t.TempDir(), "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = s.Close() })
	return s
}

func TestCreateTwiceIsAlreadyExists(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	p := domain.Product{SKU: "PEN-001", Name: "ปากกา", Qty: 1}
	if err := s.Create(ctx, p); err != nil {
		t.Fatal(err)
	}

	err := s.Create(ctx, p)

	if !errors.Is(err, domain.ErrAlreadyExists) {
		t.Fatalf("err = %v, want ErrAlreadyExists", err)
	}
}

func TestIssueDistinguishesNotFoundFromInsufficient(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	if err := s.Create(ctx, domain.Product{SKU: "PEN-001", Name: "ปากกา", Qty: 2}); err != nil {
		t.Fatal(err)
	}

	if _, err := s.Issue(ctx, "NOPE-1", 1); !errors.Is(err, domain.ErrNotFound) {
		t.Errorf("unknown sku: err = %v, want ErrNotFound", err)
	}
	if _, err := s.Issue(ctx, "PEN-001", 3); !errors.Is(err, domain.ErrInsufficientStock) {
		t.Errorf("too many: err = %v, want ErrInsufficientStock", err)
	}
}

// เทสต์ที่พิสูจน์ว่า "เบิกแบบ atomic" กันเบิกเกินได้จริงเมื่อมีคำขอพร้อมกัน
// 20 goroutine แย่งเบิกทีละ 1 จากสต็อก 10 → ต้องสำเร็จ 10 พอดี และคงเหลือ 0 (ไม่ติดลบ)
func TestConcurrentIssueNeverOversells(t *testing.T) {
	ctx := context.Background()
	s := openTemp(t)
	if err := s.Create(ctx, domain.Product{SKU: "PEN-001", Name: "ปากกา", Qty: 10}); err != nil {
		t.Fatal(err)
	}

	var wg sync.WaitGroup
	var mu sync.Mutex
	ok := 0
	for range 20 {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if _, err := s.Issue(ctx, "PEN-001", 1); err == nil {
				mu.Lock()
				ok++
				mu.Unlock()
			}
		}()
	}
	wg.Wait()

	p, err := s.Get(ctx, "PEN-001")
	if err != nil {
		t.Fatal(err)
	}
	if ok != 10 || p.Qty != 0 {
		t.Errorf("successful issues = %d, qty = %d; want 10 and 0", ok, p.Qty)
	}
}

func TestListEmptyIsEmptySliceNotNil(t *testing.T) {
	got, err := openTemp(t).List(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if got == nil || len(got) != 0 {
		t.Errorf("List() = %#v, want empty non-nil slice", got)
	}
}
