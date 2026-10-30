// Package app = ลำดับงาน (use case) — ตรวจกฎด้วย domain แล้วสั่งที่เก็บข้อมูลผ่าน port
// เทียบ C#: StockApi/Application/StockService.cs
package app

import (
	"context"
	"log/slog"

	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// Repository = port ขาออก: "ที่เก็บสินค้า" — service ไม่รู้ว่าเป็น SQLite หรือของปลอมในเทสต์
// ประกาศที่ฝั่งคนใช้ (ที่นี่) · C#: interface IProductRepository ใน Application
type Repository interface {
	Create(ctx context.Context, p domain.Product) error          // มีแล้ว → domain.ErrAlreadyExists
	Get(ctx context.Context, sku string) (domain.Product, error) // ไม่มี → domain.ErrNotFound
	List(ctx context.Context) ([]domain.Product, error)
	Search(ctx context.Context, q string) ([]domain.Product, error)
	Receive(ctx context.Context, sku string, qty int) (domain.Product, error)
	// Issue ต้องกันเบิกเกินแบบ atomic (ทำในคำสั่งเดียวที่ฐานข้อมูล) → domain.ErrInsufficientStock
	Issue(ctx context.Context, sku string, qty int) (domain.Product, error)
}

// Service = use case ทั้งหมดของสต็อก
type Service struct {
	repo Repository
	log  *slog.Logger
}

// NewService รับของที่ต้องใช้ทาง parameter (constructor injection) — C#: ctor ที่ DI container เติมให้
func NewService(repo Repository, log *slog.Logger) *Service {
	return &Service{repo: repo, log: log}
}

// Create ตรวจกฎผ่าน domain.NewProduct ก่อนบันทึก — ข้อมูลผิดกฎไม่ถึงฐานข้อมูลเลย
func (s *Service) Create(ctx context.Context, sku, name string, qty int, price int64) (domain.Product, error) {
	p, err := domain.NewProduct(sku, name, qty, price)
	if err != nil {
		return domain.Product{}, err
	}
	if err := s.repo.Create(ctx, p); err != nil {
		return domain.Product{}, err
	}
	s.log.Info("สร้างสินค้า", "sku", p.SKU, "qty", p.Qty, "price", p.Price)
	return p, nil
}

// Get คืนสินค้า 1 ตัว
func (s *Service) Get(ctx context.Context, sku string) (domain.Product, error) {
	return s.repo.Get(ctx, sku)
}

// List คืนสินค้าทั้งหมด
func (s *Service) List(ctx context.Context) ([]domain.Product, error) {
	return s.repo.List(ctx)
}

// Receive รับของเข้าคลัง
func (s *Service) Receive(ctx context.Context, sku string, qty int) (domain.Product, error) {
	if err := domain.ValidateMove(qty); err != nil {
		return domain.Product{}, err
	}
	p, err := s.repo.Receive(ctx, sku, qty)
	if err != nil {
		return domain.Product{}, err
	}
	s.log.Info("รับเข้า", "sku", sku, "qty", qty, "balance", p.Qty)
	return p, nil
}

// Issue เบิกของออก — เบิกเกินคงเหลือได้ ErrInsufficientStock และ log เป็น Warn (ไม่ใช่ error ของระบบ แต่น่าจับตา)
func (s *Service) Issue(ctx context.Context, sku string, qty int) (domain.Product, error) {
	if err := domain.ValidateMove(qty); err != nil {
		return domain.Product{}, err
	}
	p, err := s.repo.Issue(ctx, sku, qty)
	if err != nil {
		s.log.Warn("เบิกไม่สำเร็จ", "sku", sku, "qty", qty, "err", err)
		return domain.Product{}, err
	}
	s.log.Info("เบิกออก", "sku", sku, "qty", qty, "balance", p.Qty)
	return p, nil
}

// Search ค้นสินค้าตามชื่อ
func (s *Service) Search(ctx context.Context, q string) ([]domain.Product, error) {
	return s.repo.Search(ctx, q)
}
