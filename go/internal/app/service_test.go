package app

import (
	"context"
	"errors"
	"log/slog"
	"testing"

	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// memRepo = ของปลอมของ Repository ในหน่วยความจำ — เขียนเองไม่กี่บรรทัด (C#: แทน Moq)
type memRepo struct {
	items map[string]domain.Product
}

func newMemRepo() *memRepo { return &memRepo{items: map[string]domain.Product{}} }

func (m *memRepo) Create(_ context.Context, p domain.Product) error {
	if _, ok := m.items[p.SKU]; ok {
		return domain.ErrAlreadyExists
	}
	m.items[p.SKU] = p
	return nil
}

func (m *memRepo) Get(_ context.Context, sku string) (domain.Product, error) {
	p, ok := m.items[sku]
	if !ok {
		return domain.Product{}, domain.ErrNotFound
	}
	return p, nil
}

func (m *memRepo) List(_ context.Context) ([]domain.Product, error) {
	out := []domain.Product{}
	for _, p := range m.items {
		out = append(out, p)
	}
	return out, nil
}

func (m *memRepo) Receive(_ context.Context, sku string, qty int) (domain.Product, error) {
	p, ok := m.items[sku]
	if !ok {
		return domain.Product{}, domain.ErrNotFound
	}
	p.Qty += qty
	m.items[sku] = p
	return p, nil
}

func (m *memRepo) Issue(_ context.Context, sku string, qty int) (domain.Product, error) {
	p, ok := m.items[sku]
	if !ok {
		return domain.Product{}, domain.ErrNotFound
	}
	if p.Qty < qty {
		return domain.Product{}, domain.ErrInsufficientStock
	}
	p.Qty -= qty
	m.items[sku] = p
	return p, nil
}

var quiet = slog.New(slog.DiscardHandler)

func TestCreateRejectsInvalidProductBeforeSaving(t *testing.T) {
	// Arrange
	repo := newMemRepo()
	svc := NewService(repo, quiet)

	// Act
	_, err := svc.Create(context.Background(), "x", "ปากกา", 1, 0)

	// Assert — ผิดกฎ = ไม่ถึงที่เก็บเลย
	if !errors.Is(err, domain.ErrInvalid) {
		t.Fatalf("err = %v, want ErrInvalid", err)
	}
	if len(repo.items) != 0 {
		t.Errorf("saved %d items, want 0", len(repo.items))
	}
}

func TestReceiveThenIssueUpdatesBalance(t *testing.T) {
	ctx := context.Background()
	svc := NewService(newMemRepo(), quiet)
	if _, err := svc.Create(ctx, "PEN-001", "ปากกา", 5, 1500); err != nil {
		t.Fatal(err)
	}

	if _, err := svc.Receive(ctx, "PEN-001", 10); err != nil {
		t.Fatal(err)
	}
	p, err := svc.Issue(ctx, "PEN-001", 12)

	if err != nil {
		t.Fatal(err)
	}
	if p.Qty != 3 {
		t.Errorf("Qty = %d, want 3 (5 + 10 - 12)", p.Qty)
	}
}

func TestIssueMoreThanBalanceFailsAndKeepsBalance(t *testing.T) {
	ctx := context.Background()
	repo := newMemRepo()
	svc := NewService(repo, quiet)
	if _, err := svc.Create(ctx, "PEN-001", "ปากกา", 2, 1500); err != nil {
		t.Fatal(err)
	}

	_, err := svc.Issue(ctx, "PEN-001", 3)

	if !errors.Is(err, domain.ErrInsufficientStock) {
		t.Fatalf("err = %v, want ErrInsufficientStock", err)
	}
	if got := repo.items["PEN-001"].Qty; got != 2 {
		t.Errorf("Qty = %d, want 2 (unchanged)", got)
	}
}

func TestMoveWithZeroQtyIsInvalid(t *testing.T) {
	svc := NewService(newMemRepo(), quiet)
	if _, err := svc.Receive(context.Background(), "PEN-001", 0); !errors.Is(err, domain.ErrInvalid) {
		t.Errorf("Receive 0: err = %v, want ErrInvalid", err)
	}
}

// Search ของปลอม — เทสต์ของ service ไม่ได้ใช้ แต่ต้องมีให้ครบตาม interface (ไม่งั้น compile ไม่ผ่าน)
func (m *memRepo) Search(_ context.Context, _ string) ([]domain.Product, error) {
	return []domain.Product{}, nil
}
