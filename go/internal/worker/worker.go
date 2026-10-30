// Package worker = ตัวทำงานเบื้องหลัง วนหยิบ order จากคิวมาทำจนกว่าจะสั่งปิด
// เทียบ C#: StockApi/Worker/OrderWorker.cs (BackgroundService)
package worker

import (
	"context"
	"log/slog"
	"time"
)

// Processor = อะไรก็ได้ที่ "ทำงานถัดไป 1 ชิ้น" ได้ (*app.OrderService) · ประกาศ interface ฝั่งคนใช้ → เทสต์ใส่ของปลอมได้
type Processor interface {
	ProcessNext(ctx context.Context) (bool, error)
	RecoverStuck(ctx context.Context) (int64, error)
}

// Run บล็อกจน ctx ถูกยกเลิก · เรียกด้วย go worker.Run(...) ใน main
//
// รอบหนึ่ง: ทำงานติดกันจนคิวว่าง → พัก interval → วนใหม่
// ทำไมพัก: ไม่มีงานก็ถามฐานข้อมูลถี่ๆ เปลือง CPU เปล่าๆ · 500ms = order ใหม่รอไม่เกินครึ่งวินาที
// ponytail: ถามซ้ำเป็นรอบ (polling) · ถ้าต้องการไวกว่านี้ค่อยเพิ่มสัญญาณ "มีงานใหม่" จาก Place มาปลุก worker
func Run(ctx context.Context, p Processor, interval time.Duration, log *slog.Logger) {
	log.Info("worker started", "interval", interval.String())
	broken := false // รอบก่อนพัง = อาจมี order ค้าง processing (ย้ายสถานะไม่สำเร็จ) ต้องคืนก่อนทำต่อ
	for {
		if broken {
			broken = !recoverStuck(ctx, p, log)
		}
		if !broken {
			broken = !drain(ctx, p, log)
		}
		select { // รออย่างใดอย่างหนึ่ง: ครบเวลาพัก หรือ สั่งปิด
		case <-ctx.Done():
			log.Info("worker stopped")
			return
		case <-time.After(interval):
		}
	}
}

// drain ทำงานจนคิวว่าง · เช็ค ctx ระหว่างงาน ไม่ใช่กลางงาน
//
// context.WithoutCancel = งานที่เริ่มไปแล้วทำต่อจนจบแม้จะสั่งปิด (ไม่ตัดกลาง transaction)
// ทางที่ไม่เลือก: ส่ง ctx ตรงๆ → กด Ctrl+C ตอนตัดสต็อก transaction ถูกยกเลิก order ค้าง processing
// (ไม่เสียหาย เพราะ RecoverStuck จะคืนเข้าคิวตอนเปิดใหม่ — แต่ปิดเรียบร้อยดีกว่า)
// คืน false = ระบบมีปัญหา
func drain(ctx context.Context, p Processor, log *slog.Logger) bool {
	for ctx.Err() == nil {
		ok, err := p.ProcessNext(context.WithoutCancel(ctx))
		if err != nil {
			log.Error("worker", "err", err) // ระบบมีปัญหา → พักรอบนี้ไปก่อน รอบหน้าค่อยลองใหม่
			return false
		}
		if !ok {
			return true // คิวว่าง
		}
	}
	return true
}

// recoverStuck คืน order ที่ค้าง processing เข้าคิว · คืน false = ยังทำไม่ได้ (ฐานข้อมูลยังมีปัญหา) รอบหน้าลองใหม่
// ปลอดภัยเพราะเรียกระหว่างรอบ = worker ตัวนี้ไม่ได้ถืองานไหนอยู่
// shortcut: คืนทุก order ที่ processing ได้เพราะมี worker ตัวเดียว, ถ้ารันหลาย instance ต้องเปลี่ยนเป็น lease (ดู store.RecoverStuck)
func recoverStuck(ctx context.Context, p Processor, log *slog.Logger) bool {
	n, err := p.RecoverStuck(context.WithoutCancel(ctx))
	if err != nil {
		log.Error("worker คืนงานค้างไม่ได้", "err", err)
		return false
	}
	if n > 0 {
		log.Warn("คืนงานค้างเข้าคิว", "orders", n)
	}
	return true
}
