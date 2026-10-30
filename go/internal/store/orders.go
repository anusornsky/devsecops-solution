// เก็บคำสั่งซื้อใน SQLite — เทียบ C#: StockApi/Infrastructure/EfOrderRepository.cs
package store

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"math"
	"time"

	"github.com/anusornsky/go-csharp-lab/go/internal/app"
	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

var _ app.OrderRepository = (*SQLite)(nil)

func nowMs() int64 { return time.Now().UnixMilli() }

// CreateOrder บันทึก order + ทุกรายการใน transaction เดียว — ไม่มีทางเกิด order ที่มีรายการครึ่งเดียว
func (s *SQLite) CreateOrder(ctx context.Context, o domain.Order) (domain.Order, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return domain.Order{}, err
	}
	// defer Rollback = "ถ้าออกจากฟังก์ชันโดยยังไม่ Commit ให้ยกเลิกทั้งหมด" · Commit ไปแล้วเรียกซ้ำก็ไม่มีผล
	// C#: await using var tx = ... (Dispose โดยไม่ Commit = rollback)
	defer tx.Rollback()

	res, err := tx.ExecContext(ctx, `INSERT INTO orders (status, run_at) VALUES (?, ?)`, domain.StatusPending, nowMs())
	if err != nil {
		return domain.Order{}, err
	}
	if o.ID, err = res.LastInsertId(); err != nil { // id ที่ฐานข้อมูลเพิ่งสร้าง (AUTOINCREMENT)
		return domain.Order{}, err
	}
	for _, it := range o.Items {
		if _, err := tx.ExecContext(ctx, `INSERT INTO order_items (order_id, sku, qty) VALUES (?, ?, ?)`, o.ID, it.SKU, it.Qty); err != nil {
			return domain.Order{}, err
		}
	}
	return o, tx.Commit()
}

// GetOrder อ่าน order + รายการ · ⚠️ ห้ามเรียกจากใน transaction (ใช้ s.db ไม่ใช่ tx — connection มีเส้นเดียว จะค้างรอตัวเอง)
func (s *SQLite) GetOrder(ctx context.Context, id int64) (domain.Order, error) {
	o := domain.Order{ID: id}
	err := s.db.QueryRowContext(ctx, `SELECT status, total, reason, attempts FROM orders WHERE id = ?`, id).
		Scan(&o.Status, &o.Total, &o.Reason, &o.Attempts)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Order{}, domain.ErrOrderNotFound
	}
	if err != nil {
		return domain.Order{}, err
	}
	rows, err := s.db.QueryContext(ctx, `SELECT sku, qty, unit_price FROM order_items WHERE order_id = ? ORDER BY sku`, id)
	if err != nil {
		return domain.Order{}, err
	}
	defer rows.Close()
	o.Items = []domain.OrderItem{}
	for rows.Next() {
		var it domain.OrderItem
		if err := rows.Scan(&it.SKU, &it.Qty, &it.UnitPrice); err != nil {
			return domain.Order{}, err
		}
		o.Items = append(o.Items, it)
	}
	return o, rows.Err()
}

// ClaimNext: หา order ที่รอคิวเก่าสุด → "จอง" ด้วย UPDATE ที่มีเงื่อนไข status = 'pending'
// เทคนิคเดียวกับเบิกสต็อก: ถ้ามี worker อื่น (เช่นรันไว้ 2 เครื่อง) จองตัวเดียวกันไปก่อน เราจะแก้ได้ 0 แถว → ไปหาตัวถัดไป
func (s *SQLite) ClaimNext(ctx context.Context) (domain.Order, bool, error) {
	for {
		var id int64
		err := s.db.QueryRowContext(ctx,
			`SELECT id FROM orders WHERE status = ? AND run_at <= ? ORDER BY id LIMIT 1`, domain.StatusPending, nowMs()).Scan(&id)
		if errors.Is(err, sql.ErrNoRows) {
			return domain.Order{}, false, nil // คิวว่าง
		}
		if err != nil {
			return domain.Order{}, false, err
		}
		res, err := s.db.ExecContext(ctx,
			`UPDATE orders SET status = ?, attempts = attempts + 1 WHERE id = ? AND status = ?`,
			domain.StatusProcessing, id, domain.StatusPending)
		if err != nil {
			return domain.Order{}, false, err
		}
		if n, _ := res.RowsAffected(); n == 1 {
			o, err := s.GetOrder(ctx, id)
			return o, err == nil, err
		}
		// n == 0: มีคนจองตัดหน้า → วนหาตัวถัดไป
	}
}

// Fulfil = หัวใจของระบบ order: ทุกขั้นอยู่ใน transaction เดียว
//  1. ตัดสต็อกทีละรายการ (UPDATE ... AND qty >= ? เหมือน Issue)
//  2. จำราคาลง order_items
//  3. order → done + ยอดรวม
//
// รายการไหนไม่ผ่าน → return error → defer Rollback คืนสต็อกรายการก่อนหน้าให้เองทั้งหมด
// ทางที่ไม่เลือก: ตัดทีละรายการแยกกัน แล้วเขียนโค้ด "คืนสต็อก" เองตอนพัง — ถ้าโปรแกรมตายระหว่างคืน ของหายถาวร
func (s *SQLite) Fulfil(ctx context.Context, id int64) (int64, error) {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return 0, err
	}
	defer tx.Rollback()

	items, err := orderItemsTx(ctx, tx, id)
	if err != nil {
		return 0, err
	}

	var total int64
	for _, it := range items {
		res, err := tx.ExecContext(ctx, `UPDATE products SET qty = qty - ? WHERE sku = ? AND qty >= ?`, it.Qty, it.SKU, it.Qty)
		if err != nil {
			return 0, err
		}
		if n, _ := res.RowsAffected(); n == 0 {
			return 0, whyNotIssued(ctx, tx, it)
		}
		var price int64
		if err := tx.QueryRowContext(ctx, `SELECT price FROM products WHERE sku = ?`, it.SKU).Scan(&price); err != nil {
			return 0, err
		}
		if _, err := tx.ExecContext(ctx, `UPDATE order_items SET unit_price = ? WHERE order_id = ? AND sku = ?`, price, id, it.SKU); err != nil {
			return 0, err
		}
		// เช็คก่อนคูณ/บวก: price*qty + total > MaxInt64 ⇔ qty > (MaxInt64-total)/price · Go ไม่ร้องเตือน overflow เอง (C# ใช้ checked)
		if price > 0 && int64(it.Qty) > (math.MaxInt64-total)/price {
			return 0, domain.ErrTotalTooLarge
		}
		total += price * int64(it.Qty)
	}

	// WHERE status = 'processing' = กันพลาด: ถ้ามีใครเปลี่ยนสถานะไปแล้ว อย่าทับ (ยกเลิกทั้ง transaction)
	res, err := tx.ExecContext(ctx, `UPDATE orders SET status = ?, total = ?, reason = '' WHERE id = ? AND status = ?`,
		domain.StatusDone, total, id, domain.StatusProcessing)
	if err != nil {
		return 0, err
	}
	if n, _ := res.RowsAffected(); n != 1 {
		return 0, fmt.Errorf("order %d ไม่ได้อยู่ในสถานะ processing แล้ว", id)
	}
	return total, tx.Commit()
}

// orderItemsTx อ่านรายการทั้งหมดให้จบก่อน (ปิด rows) แล้วค่อยสั่ง UPDATE — อ่านไปเขียนไปบน connection เดียวกันไม่ได้
func orderItemsTx(ctx context.Context, tx *sql.Tx, id int64) ([]domain.OrderItem, error) {
	rows, err := tx.QueryContext(ctx, `SELECT sku, qty FROM order_items WHERE order_id = ? ORDER BY sku`, id)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var items []domain.OrderItem
	for rows.Next() {
		var it domain.OrderItem
		if err := rows.Scan(&it.SKU, &it.Qty); err != nil {
			return nil, err
		}
		items = append(items, it)
	}
	return items, rows.Err()
}

// whyNotIssued แยกว่าตัดไม่ได้เพราะไม่มีสินค้า หรือของไม่พอ — ข้อความนี้จะไปอยู่ใน reason ให้ลูกค้าเห็น
func whyNotIssued(ctx context.Context, tx *sql.Tx, it domain.OrderItem) error {
	var have int
	err := tx.QueryRowContext(ctx, `SELECT qty FROM products WHERE sku = ?`, it.SKU).Scan(&have)
	if errors.Is(err, sql.ErrNoRows) {
		return fmt.Errorf("%w: %s", domain.ErrNotFound, it.SKU)
	}
	if err != nil {
		return err
	}
	return fmt.Errorf("%w: %s (มี %d ต้องการ %d)", domain.ErrInsufficientStock, it.SKU, have, it.Qty)
}

func (s *SQLite) MarkFailed(ctx context.Context, id int64, reason string) error {
	return s.moveFromProcessing(ctx, id, `status = 'failed', reason = ?`, reason)
}

func (s *SQLite) Requeue(ctx context.Context, id int64, delay time.Duration) error {
	return s.moveFromProcessing(ctx, id, `status = 'pending', run_at = ?`, nowMs()+delay.Milliseconds())
}

// moveFromProcessing เปลี่ยนสถานะเฉพาะ order ที่ยัง processing อยู่ — set มาจากโค้ดเราเท่านั้น (ไม่ใช่ข้อมูลจาก client) จึงต่อ string ได้
func (s *SQLite) moveFromProcessing(ctx context.Context, id int64, set string, arg any) error {
	res, err := s.db.ExecContext(ctx, `UPDATE orders SET `+set+` WHERE id = ? AND status = 'processing'`, arg, id)
	if err != nil {
		return err
	}
	if n, _ := res.RowsAffected(); n != 1 {
		return fmt.Errorf("order %d ไม่ได้อยู่ในสถานะ processing แล้ว", id)
	}
	return nil
}

// RecoverStuck เรียกตอนเปิดโปรแกรม: order ที่ค้าง processing = โปรแกรมรอบก่อนตายกลางงาน
// (transaction ของงานนั้นถูก rollback ไปแล้ว สต็อกไม่ได้ถูกตัด) → ปล่อยกลับเข้าคิว
// ponytail: ถือว่ามี worker ตัวเดียว · ถ้ารันหลายเครื่องพร้อมกัน จะไปแย่งงานที่อีกเครื่องกำลังทำอยู่
// → ตอนนั้นค่อยเพิ่มคอลัมน์ "จองถึงเวลา" (lease) แล้วคืนเฉพาะงานที่หมดเวลาจอง
func (s *SQLite) RecoverStuck(ctx context.Context) (int64, error) {
	res, err := s.db.ExecContext(ctx, `UPDATE orders SET status = 'pending' WHERE status = 'processing'`)
	if err != nil {
		return 0, err
	}
	return res.RowsAffected()
}
