// Package store = adapter ขาออก: เก็บสินค้าใน SQLite ด้วย database/sql + SQL ที่เขียนเอง
// เทียบ C#: StockApi/Infrastructure/EfProductRepository.cs (ฝั่ง C# ใช้ EF Core)
package store

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"strings"

	_ "modernc.org/sqlite" // import เพื่อให้ driver "sqlite" ลงทะเบียนตัวเอง (ไม่ได้เรียกชื่อตรงๆ จึงใช้ _)

	"github.com/anusornsky/go-csharp-lab/go/internal/app"
	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// SQLite = app.Repository ที่ใช้ไฟล์ SQLite
type SQLite struct{ db *sql.DB }

// เช็คตอน compile ว่า *SQLite มี method ครบตาม port
var _ app.Repository = (*SQLite)(nil)

// ⚠️ ไฟล์ stock.db ที่สร้างก่อนมีช่อง price ต้องลบทิ้ง — CREATE TABLE IF NOT EXISTS ไม่แก้ตารางที่มีอยู่แล้ว
// (นี่คือเหตุผลที่งานจริงต้องมีเครื่องมือ migration: สั่ง "เพิ่มคอลัมน์" กับฐานข้อมูลที่มีข้อมูลอยู่แล้วได้)
const schema = `
CREATE TABLE IF NOT EXISTS products (
	sku   TEXT PRIMARY KEY,
	name  TEXT NOT NULL,
	qty   INTEGER NOT NULL CHECK (qty >= 0),
	price INTEGER NOT NULL DEFAULT 0 CHECK (price >= 0)
);
CREATE TABLE IF NOT EXISTS orders (
	id       INTEGER PRIMARY KEY AUTOINCREMENT,
	status   TEXT NOT NULL CHECK (status IN ('pending', 'processing', 'done', 'failed')),
	total    INTEGER NOT NULL DEFAULT 0,
	reason   TEXT NOT NULL DEFAULT '',
	attempts INTEGER NOT NULL DEFAULT 0,
	run_at   INTEGER NOT NULL DEFAULT 0 -- เวลาที่ worker หยิบได้ (unix ms) · ใช้หน่วงตอนลองใหม่
);
-- worker ถาม "pending ที่ถึงเวลาแล้ว ORDER BY id" ทุก 500ms · ไม่มี index = สแกนทุก order ที่เคยมี (done สะสมไปเรื่อยๆ)
-- (status, id) ไม่ใส่ run_at: ได้ id เรียงมาจาก index เลย ไม่ต้องเรียงใหม่ · ข้าม pending ที่ยังไม่ถึงเวลา (มีไม่กี่ตัว) เอา
CREATE INDEX IF NOT EXISTS idx_orders_queue ON orders (status, id);
CREATE TABLE IF NOT EXISTS order_items (
	order_id   INTEGER NOT NULL REFERENCES orders (id),
	sku        TEXT NOT NULL, -- ไม่ผูก FK กับ products: สั่ง SKU ที่ไม่มีได้ worker จะตีเป็น failed
	qty        INTEGER NOT NULL CHECK (qty > 0),
	unit_price INTEGER NOT NULL DEFAULT 0, -- จำราคาตอนตัดสต็อก · ขึ้นราคาทีหลังบิลเก่าไม่เปลี่ยน
	PRIMARY KEY (order_id, sku)
)`

// Open เปิดไฟล์ฐานข้อมูลแล้วสร้างตารางถ้ายังไม่มี
// ponytail: สร้างตารางตอนเริ่มโปรแกรมแทน migration — พอสำหรับตารางเดียว · ถ้าตารางเริ่มเปลี่ยนรูปบ่อยค่อยใช้เครื่องมือ migration
// CHECK (qty >= 0) = ฐานข้อมูลกันติดลบอีกชั้น แม้โค้ดจะพลาด
func Open(ctx context.Context, path string) (*SQLite, error) {
	db, err := sql.Open("sqlite", path)
	if err != nil {
		return nil, fmt.Errorf("เปิด %s: %w", path, err)
	}
	db.SetMaxOpenConns(1) // SQLite เขียนได้ทีละคนอยู่แล้ว — connection เดียวกันกัน "database is locked"
	if _, err := db.ExecContext(ctx, schema); err != nil {
		_ = db.Close()
		return nil, fmt.Errorf("สร้างตาราง: %w", err)
	}
	return &SQLite{db: db}, nil
}

// Close ปิดไฟล์ฐานข้อมูล
func (s *SQLite) Close() error { return s.db.Close() }

// Ping เช็คว่าฐานข้อมูลยังตอบ — ใช้ใน /healthz
func (s *SQLite) Ping(ctx context.Context) error { return s.db.PingContext(ctx) }

// Create เพิ่มสินค้า · SKU ซ้ำ = ErrAlreadyExists (อ่านจาก error ของ PRIMARY KEY)
func (s *SQLite) Create(ctx context.Context, p domain.Product) error {
	_, err := s.db.ExecContext(ctx, `INSERT INTO products (sku, name, qty, price) VALUES (?, ?, ?, ?)`, p.SKU, p.Name, p.Qty, p.Price)
	if err != nil && strings.Contains(err.Error(), "UNIQUE constraint failed") {
		return domain.ErrAlreadyExists
	}
	return err
}

// Get อ่านสินค้า 1 ตัว · ไม่มีแถว = ErrNotFound
func (s *SQLite) Get(ctx context.Context, sku string) (domain.Product, error) {
	var p domain.Product
	err := s.db.QueryRowContext(ctx, `SELECT sku, name, qty, price FROM products WHERE sku = ?`, sku).Scan(&p.SKU, &p.Name, &p.Qty, &p.Price)
	if errors.Is(err, sql.ErrNoRows) {
		return domain.Product{}, domain.ErrNotFound
	}
	return p, err
}

// List อ่านทั้งหมด เรียงตาม SKU · ว่าง = [] (ไม่ใช่ nil → JSON เป็น [] ไม่ใช่ null)
func (s *SQLite) List(ctx context.Context) ([]domain.Product, error) {
	rows, err := s.db.QueryContext(ctx, `SELECT sku, name, qty, price FROM products ORDER BY sku`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []domain.Product{}
	for rows.Next() {
		var p domain.Product
		if err := rows.Scan(&p.SKU, &p.Name, &p.Qty, &p.Price); err != nil {
			return nil, err
		}
		out = append(out, p)
	}
	return out, rows.Err() // ⚠️ Next() คืน false ทั้งตอนอ่านจบและตอนพัง — ต้องถาม Err()
}

// Receive บวกจำนวนในคำสั่งเดียว (ไม่อ่านก่อนแล้วเขียนทับ — กันคำขอพร้อมกันเขียนทับกันเอง)
func (s *SQLite) Receive(ctx context.Context, sku string, qty int) (domain.Product, error) {
	res, err := s.db.ExecContext(ctx, `UPDATE products SET qty = qty + ? WHERE sku = ?`, qty, sku)
	if err != nil {
		return domain.Product{}, err
	}
	if n, _ := res.RowsAffected(); n == 0 {
		return domain.Product{}, domain.ErrNotFound
	}
	return s.Get(ctx, sku)
}

// Issue ลบจำนวนแบบ atomic: เงื่อนไข qty >= ? อยู่ในคำสั่ง UPDATE เดียวกัน
// ทางที่ไม่เลือก: อ่าน qty มาเช็คในโค้ดแล้วค่อย UPDATE — คำขอ 2 ตัวมาพร้อมกันจะผ่านการเช็คทั้งคู่ แล้วเบิกเกินได้
// แก้ 0 แถว = สินค้าไม่มี หรือสต็อกไม่พอ → อ่านซ้ำเพื่อแยกกรณี
func (s *SQLite) Issue(ctx context.Context, sku string, qty int) (domain.Product, error) {
	res, err := s.db.ExecContext(ctx, `UPDATE products SET qty = qty - ? WHERE sku = ? AND qty >= ?`, qty, sku, qty)
	if err != nil {
		return domain.Product{}, err
	}
	if n, _ := res.RowsAffected(); n == 0 {
		if _, err := s.Get(ctx, sku); err != nil {
			return domain.Product{}, err // ErrNotFound
		}
		return domain.Product{}, domain.ErrInsufficientStock
	}
	return s.Get(ctx, sku)
}

// queryProducts รัน SELECT แล้วอ่านทุกแถวเป็น []domain.Product (เหมือน List แต่รับ SQL เข้ามา)
func (s *SQLite) queryProducts(ctx context.Context, query string, args ...any) ([]domain.Product, error) {
	rows, err := s.db.QueryContext(ctx, query, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []domain.Product{}
	for rows.Next() {
		var p domain.Product
		if err := rows.Scan(&p.SKU, &p.Name, &p.Qty, &p.Price); err != nil {
			return nil, err
		}
		out = append(out, p)
	}
	return out, rows.Err()
}

// Search ✅ ใช้ parameter (?) — driver เติมค่าให้ ข้อมูลไม่มีทางกลายเป็นคำสั่ง
func (s *SQLite) Search(ctx context.Context, q string) ([]domain.Product, error) {
	return s.queryProducts(ctx, "SELECT sku, name, qty, price FROM products WHERE name LIKE '%' || ? || '%'", q)
}
