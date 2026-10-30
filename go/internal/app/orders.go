// ลำดับงานของคำสั่งซื้อ: รับ order (เร็ว ตอบทันที) + ประมวลผลทีหลัง (worker เรียก ProcessNext)
// เทียบ C#: StockApi/Application/OrderService.cs
package app

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"time"

	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// MaxAttempts = ลองได้กี่ครั้งถ้าพังแบบชั่วคราว (ฐานข้อมูลไม่ตอบ ฯลฯ) ก่อนยอมแพ้
const MaxAttempts = 3

// OrderRepository = port ขาออกของ order · ทุก method ที่เปลี่ยนสถานะต้องเช็คสถานะเดิมใน WHERE (กันสองคนแก้ทับกัน)
type OrderRepository interface {
	CreateOrder(ctx context.Context, o domain.Order) (domain.Order, error) // คืน order ที่มี ID แล้ว
	GetOrder(ctx context.Context, id int64) (domain.Order, error)          // ไม่มี → domain.ErrOrderNotFound
	// ClaimNext หยิบ order pending ที่ถึงเวลาแล้ว 1 ตัว → processing + attempts+1 · ไม่มีงาน = ok false
	ClaimNext(ctx context.Context) (o domain.Order, ok bool, err error)
	// Fulfil ตัดสต็อกทุกรายการ + จำราคา + done ใน transaction เดียว (สำเร็จทั้งหมด หรือไม่เปลี่ยนอะไรเลย)
	// สินค้าไม่มี / ไม่พอ → error ที่ห่อ domain.ErrNotFound / domain.ErrInsufficientStock
	Fulfil(ctx context.Context, id int64) (total int64, err error)
	MarkFailed(ctx context.Context, id int64, reason string) error
	Requeue(ctx context.Context, id int64, delay time.Duration) error // กลับไป pending แต่ห้ามหยิบจนครบเวลา delay
	RecoverStuck(ctx context.Context) (int64, error)                  // processing ค้าง (โปรแกรมตายกลางงาน) → pending
}

type OrderService struct {
	repo OrderRepository
	log  *slog.Logger
}

func NewOrderService(repo OrderRepository, log *slog.Logger) *OrderService {
	return &OrderService{repo: repo, log: log}
}

// Place รับ order แล้วตอบทันที (ไม่รอตัดสต็อก) — client ได้ 202 + id ไว้ถามสถานะทีหลัง
// ทำไมไม่ตัดสต็อกเลยตรงนี้: งานจริงตอนประมวลผลมักมีขั้นช้า (ตัดบัตร, เรียกระบบขนส่ง) → ไม่ให้ client รอ
func (s *OrderService) Place(ctx context.Context, items []domain.OrderItem) (domain.Order, error) {
	o, err := domain.NewOrder(items)
	if err != nil {
		return domain.Order{}, err
	}
	o, err = s.repo.CreateOrder(ctx, o)
	if err != nil {
		return domain.Order{}, err
	}
	s.log.Info("รับคำสั่งซื้อ", "order", o.ID, "items", len(o.Items))
	return o, nil
}

func (s *OrderService) Get(ctx context.Context, id int64) (domain.Order, error) {
	return s.repo.GetOrder(ctx, id)
}

// RecoverStuck คืน order ที่ค้าง processing เข้าคิว — worker เรียกหลังรอบที่พัง
func (s *OrderService) RecoverStuck(ctx context.Context) (int64, error) {
	return s.repo.RecoverStuck(ctx)
}

// ProcessNext ทำ order ถัดไป 1 ตัว · คืน true = ทำไป 1 ตัว (worker ควรเรียกต่อทันที) · false = คิวว่าง
// error ที่คืน = ระบบมีปัญหา (หยิบงาน/บันทึกสถานะไม่ได้) ไม่ใช่ order ล้มเหลว
//
// หัวใจ: แยก "ล้มเหลวถาวร" กับ "ล้มเหลวชั่วคราว"
//   - สินค้าไม่มี / สต็อกไม่พอ → ลองใหม่กี่ทีก็ไม่ผ่าน → failed ทันที
//   - อย่างอื่น (ฐานข้อมูลสะดุด) → ลองใหม่ หน่วงเวลาเพิ่มขึ้นทุกครั้ง จนครบ MaxAttempts
func (s *OrderService) ProcessNext(ctx context.Context) (bool, error) {
	o, ok, err := s.repo.ClaimNext(ctx)
	if err != nil || !ok {
		return false, err
	}

	total, err := s.repo.Fulfil(ctx, o.ID)
	switch {
	case err == nil:
		s.log.Info("คำสั่งซื้อสำเร็จ", "order", o.ID, "total", total)
		return true, nil

	case errors.Is(err, domain.ErrNotFound), errors.Is(err, domain.ErrInsufficientStock), errors.Is(err, domain.ErrTotalTooLarge):
		s.log.Warn("คำสั่งซื้อล้มเหลว", "order", o.ID, "reason", err.Error())
		return true, s.repo.MarkFailed(ctx, o.ID, err.Error())

	case o.Attempts >= MaxAttempts:
		s.log.Error("ลองครบแล้ว ยอมแพ้", "order", o.ID, "attempts", o.Attempts, "err", err)
		return true, s.repo.MarkFailed(ctx, o.ID, fmt.Sprintf("ลองครบ %d ครั้งแล้ว: %v", MaxAttempts, err))

	default:
		delay := time.Duration(o.Attempts) * 2 * time.Second // backoff: ครั้งที่ 1 รอ 2 วิ · ครั้งที่ 2 รอ 4 วิ
		s.log.Warn("พังชั่วคราว จะลองใหม่", "order", o.ID, "attempt", o.Attempts, "in", delay.String(), "err", err)
		return true, s.repo.Requeue(ctx, o.ID, delay)
	}
}
