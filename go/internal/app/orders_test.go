package app

import (
	"context"
	"errors"
	"fmt"
	"testing"
	"time"

	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// fakeOrders = OrderRepository ปลอม มี order เดียว · fulfilErrs = error ที่ Fulfil จะคืนทีละครั้งตามลำดับ (หมดแล้ว = สำเร็จ)
// จดทุกการเรียกไว้ใน calls ให้เทสต์เช็คว่า service สั่งอะไรไปบ้าง
type fakeOrders struct {
	order      domain.Order
	fulfilErrs []error
	calls      []string
}

func (f *fakeOrders) CreateOrder(_ context.Context, o domain.Order) (domain.Order, error) {
	o.ID = 1
	f.order = o
	return o, nil
}

func (f *fakeOrders) GetOrder(_ context.Context, _ int64) (domain.Order, error) { return f.order, nil }

func (f *fakeOrders) ClaimNext(_ context.Context) (domain.Order, bool, error) {
	if f.order.Status != domain.StatusPending {
		return domain.Order{}, false, nil
	}
	f.order.Status = domain.StatusProcessing
	f.order.Attempts++
	return f.order, true, nil
}

func (f *fakeOrders) Fulfil(_ context.Context, _ int64) (int64, error) {
	if len(f.fulfilErrs) > 0 {
		err := f.fulfilErrs[0]
		f.fulfilErrs = f.fulfilErrs[1:]
		return 0, err
	}
	f.order.Status = domain.StatusDone
	return 3000, nil
}

func (f *fakeOrders) MarkFailed(_ context.Context, _ int64, reason string) error {
	f.order.Status, f.order.Reason = domain.StatusFailed, reason
	f.calls = append(f.calls, "failed")
	return nil
}

func (f *fakeOrders) Requeue(_ context.Context, _ int64, delay time.Duration) error {
	f.order.Status = domain.StatusPending
	f.calls = append(f.calls, "requeue "+delay.String())
	return nil
}

func (f *fakeOrders) RecoverStuck(_ context.Context) (int64, error) { return 0, nil }

// placeOne = เตรียม service + order 1 ตัวที่รอคิว
func placeOne(t *testing.T, fulfilErrs ...error) (*OrderService, *fakeOrders) {
	t.Helper()
	repo := &fakeOrders{fulfilErrs: fulfilErrs}
	svc := NewOrderService(repo, quiet)
	if _, err := svc.Place(context.Background(), []domain.OrderItem{{SKU: "PEN-001", Qty: 2}}); err != nil {
		t.Fatal(err)
	}
	return svc, repo
}

// drain เรียก ProcessNext จนคิวว่าง (ไม่สนเวลาหน่วง เพราะของปลอมไม่ได้เช็ค run_at)
func drain(t *testing.T, svc *OrderService) {
	t.Helper()
	for i := 0; i < 10; i++ {
		ok, err := svc.ProcessNext(context.Background())
		if err != nil {
			t.Fatal(err)
		}
		if !ok {
			return
		}
	}
	t.Fatal("ProcessNext ไม่หยุดภายใน 10 รอบ")
}

func TestProcessNextMarksDoneOnSuccess(t *testing.T) {
	svc, repo := placeOne(t)

	drain(t, svc)

	if repo.order.Status != domain.StatusDone {
		t.Errorf("status = %s, want done", repo.order.Status)
	}
}

// สต็อกไม่พอ = ล้มเหลวถาวร → failed ทันที ไม่เสียเวลาลองใหม่
func TestInsufficientStockFailsWithoutRetry(t *testing.T) {
	svc, repo := placeOne(t, fmt.Errorf("%w: PEN-001 (มี 1 ต้องการ 2)", domain.ErrInsufficientStock))

	drain(t, svc)

	if repo.order.Status != domain.StatusFailed || repo.order.Attempts != 1 {
		t.Errorf("status = %s attempts = %d, want failed after 1", repo.order.Status, repo.order.Attempts)
	}
}

// ยอดรวมล้น = ล้มเหลวถาวร (ราคา/จำนวนไม่เปลี่ยนเอง ลองใหม่ก็ล้นอีก) → failed ทันที
func TestTotalTooLargeFailsWithoutRetry(t *testing.T) {
	svc, repo := placeOne(t, domain.ErrTotalTooLarge)

	drain(t, svc)

	if repo.order.Status != domain.StatusFailed || repo.order.Attempts != 1 {
		t.Errorf("status = %s attempts = %d, want failed after 1", repo.order.Status, repo.order.Attempts)
	}
}

// พังชั่วคราว 1 ครั้งแล้วหาย → ลองใหม่ (หน่วง 2 วิ) แล้วสำเร็จ
func TestTransientErrorIsRetriedThenSucceeds(t *testing.T) {
	svc, repo := placeOne(t, errors.New("database is locked"))

	drain(t, svc)

	if repo.order.Status != domain.StatusDone || repo.order.Attempts != 2 {
		t.Errorf("status = %s attempts = %d, want done after 2", repo.order.Status, repo.order.Attempts)
	}
	if len(repo.calls) != 1 || repo.calls[0] != "requeue 2s" {
		t.Errorf("calls = %v, want [requeue 2s]", repo.calls)
	}
}

// พังชั่วคราวทุกครั้ง → ลองครบ MaxAttempts แล้ว failed (ไม่วนลองตลอดไป)
func TestGivesUpAfterMaxAttempts(t *testing.T) {
	boom := errors.New("disk I/O error")
	svc, repo := placeOne(t, boom, boom, boom, boom)

	drain(t, svc)

	if repo.order.Status != domain.StatusFailed || repo.order.Attempts != MaxAttempts {
		t.Errorf("status = %s attempts = %d, want failed after %d", repo.order.Status, repo.order.Attempts, MaxAttempts)
	}
	want := []string{"requeue 2s", "requeue 4s", "failed"}
	if fmt.Sprint(repo.calls) != fmt.Sprint(want) {
		t.Errorf("calls = %v, want %v", repo.calls, want)
	}
}
