package httpapi

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/anusornsky/go-csharp-lab/go/internal/app"
	"github.com/anusornsky/go-csharp-lab/go/internal/store"
	"github.com/anusornsky/go-csharp-lab/go/internal/worker"
)

// newServer = API จริงทุกชั้น + SQLite ในไฟล์ชั่วคราว — เทียบ C#: WebApplicationFactory<Program>
func newServer(t *testing.T) *httptest.Server {
	t.Helper()
	db, err := store.Open(context.Background(), filepath.Join(t.TempDir(), "api.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = db.Close() })
	quiet := slog.New(slog.DiscardHandler)
	orders := app.NewOrderService(db, quiet)

	// worker จริง พักแค่ 20ms ให้เทสต์เร็ว · Cleanup ทำย้อนลำดับ → หยุด worker ก่อนปิด DB (เหมือน main.go)
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() { worker.Run(ctx, orders, 20*time.Millisecond, quiet); close(done) }()
	t.Cleanup(func() { cancel(); <-done })

	srv := httptest.NewServer(New(app.NewService(db, quiet), orders, db, quiet))
	t.Cleanup(srv.Close)
	return srv
}

// waitOrder ถาม GET /orders/{id} ซ้ำจนสถานะไม่ใช่ pending/processing (ไม่เกิน 3 วินาที)
// งาน async ทดสอบแบบนี้: ไม่ sleep เวลาตายตัว แต่ถามจนได้ผล หรือหมดเวลา
func waitOrder(t *testing.T, base string, id int64) map[string]any {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		_, body := call(t, "GET", fmt.Sprintf("%s/orders/%d", base, id), "")
		var o map[string]any
		if err := json.Unmarshal([]byte(body), &o); err != nil {
			t.Fatalf("GET order: %s", body)
		}
		if s := o["status"]; s != "pending" && s != "processing" {
			return o
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatal("order ไม่เสร็จภายใน 3 วินาที")
	return nil
}

// placeOrder ยิง POST /orders ต้องได้ 202 + pending แล้วคืน id
func placeOrder(t *testing.T, base, body string) int64 {
	t.Helper()
	status, got := call(t, "POST", base+"/orders", body)
	var o struct {
		ID     int64  `json:"id"`
		Status string `json:"status"`
	}
	if status != http.StatusAccepted || json.Unmarshal([]byte(got), &o) != nil || o.Status != "pending" {
		t.Fatalf("POST /orders → %d %s, want 202 pending", status, got)
	}
	return o.ID
}

// เส้นทางหลักของ order: สั่ง → 202 pending → worker ทำ → done + ยอดเงิน + สต็อกลด
func TestOrderLifecycle(t *testing.T) {
	srv := newServer(t)
	call(t, "POST", srv.URL+"/products", `{"sku":"PEN-001","name":"ปากกา","qty":5,"price":1500}`)

	id := placeOrder(t, srv.URL, `{"items":[{"sku":"PEN-001","qty":2}]}`)
	o := waitOrder(t, srv.URL, id)

	if o["status"] != "done" || o["total"] != float64(3000) { // JSON ตัวเลขถูกอ่านเป็น float64 เสมอเมื่อใส่ map[string]any
		t.Errorf("order = %v, want done total 3000", o)
	}
	if _, body := call(t, "GET", srv.URL+"/products/PEN-001", ""); !strings.Contains(body, `"qty":3`) {
		t.Errorf("product = %s, want qty 3", body)
	}
}

// สต็อกไม่พอ: POST ยังได้ 202 (ตอนรับเรื่องยังไม่รู้) → worker ตีเป็น failed พร้อมเหตุผล → สต็อกไม่ถูกแตะ
func TestOrderFailsWhenStockShort(t *testing.T) {
	srv := newServer(t)
	call(t, "POST", srv.URL+"/products", `{"sku":"PEN-001","name":"ปากกา","qty":1,"price":1500}`)

	o := waitOrder(t, srv.URL, placeOrder(t, srv.URL, `{"items":[{"sku":"PEN-001","qty":2}]}`))

	if o["status"] != "failed" || o["reason"] != "สต็อกไม่พอ: PEN-001 (มี 1 ต้องการ 2)" {
		t.Errorf("order = %v, want failed สต็อกไม่พอ", o)
	}
}

func TestOrderBadInputAndNotFound(t *testing.T) {
	srv := newServer(t)
	cases := []struct {
		method, path, body string
		want               int
	}{
		{"POST", "/orders", `{"items":[]}`, 400},
		{"POST", "/orders", `{"items":[{"sku":"PEN-001","qty":0}]}`, 400},
		{"GET", "/orders/999", "", 404},
		{"GET", "/orders/abc", "", 404},
	}
	for _, c := range cases {
		if status, body := call(t, c.method, srv.URL+c.path, c.body); status != c.want {
			t.Errorf("%s %s %s → %d %s, want %d", c.method, c.path, c.body, status, body, c.want)
		}
	}
}

// id ที่ไม่ใช่ตัวเลขต้องได้ body error ภาษาไทย — คู่กับ C# OrderIdNotNumberReturnsThaiError (เทสต์บนเช็คแค่ status)
func TestOrderIdNotNumberReturnsThaiError(t *testing.T) {
	srv := newServer(t)
	// มี order id 1 จริง → " 1" (%20 = ช่องว่าง) ต้องไม่ถูกอ่านเป็น 1 (C# long.TryParse เดิมยอมช่องว่าง)
	call(t, "POST", srv.URL+"/orders", `{"items":[{"sku":"PEN-001","qty":1}]}`)

	for _, path := range []string{"/orders/abc", "/orders/%201"} {
		status, body := call(t, "GET", srv.URL+path, "")

		if status != 404 || !strings.Contains(body, "ไม่พบคำสั่งซื้อ") {
			t.Errorf("GET %s → %d %q, want 404 + ไม่พบคำสั่งซื้อ", path, status, body)
		}
	}
}

// call ยิง request แล้วคืน status + body
func call(t *testing.T, method, url, body string) (int, string) {
	t.Helper()
	req, err := http.NewRequest(method, url, strings.NewReader(body))
	if err != nil {
		t.Fatal(err)
	}
	res, err := http.DefaultClient.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	defer res.Body.Close()
	b, _ := io.ReadAll(res.Body)
	return res.StatusCode, string(b)
}

// เส้นทางหลักของงานจริง: สร้าง → รับเข้า → เบิก → เบิกเกิน → ดูยอด
func TestStockLifecycle(t *testing.T) {
	srv := newServer(t)
	steps := []struct {
		method, path, body string
		wantStatus         int
		wantBodyHas        string
	}{
		{"POST", "/products", `{"sku":"PEN-001","name":"ปากกา","qty":5}`, 201, `"qty":5`},
		{"POST", "/products", `{"sku":"PEN-001","name":"ซ้ำ","qty":1}`, 409, "มีสินค้านี้แล้ว"},
		{"POST", "/products/PEN-001/receive", `{"qty":10}`, 200, `"qty":15`},
		{"POST", "/products/PEN-001/issue", `{"qty":12}`, 200, `"qty":3`},
		{"POST", "/products/PEN-001/issue", `{"qty":4}`, 409, "สต็อกไม่พอ"},
		{"GET", "/products/PEN-001", "", 200, `"qty":3`},
		{"GET", "/products/NOPE-1", "", 404, "ไม่พบสินค้า"},
	}
	for _, s := range steps {
		status, body := call(t, s.method, srv.URL+s.path, s.body)
		if status != s.wantStatus || !strings.Contains(body, s.wantBodyHas) {
			t.Errorf("%s %s → %d %s; want %d containing %q", s.method, s.path, status, body, s.wantStatus, s.wantBodyHas)
		}
	}
}

func TestBadInputIs400(t *testing.T) {
	srv := newServer(t)
	cases := map[string]string{
		"JSON พัง": `{"sku":`,
		"ช่องที่ไม่รู้จัก": `{"sku":"PEN-001","name":"x","qty":1,"color":"red"}`,
		"sku ผิดรูปแบบ":    `{"sku":"a","name":"x","qty":1}`,
		"จำนวนเริ่มติดลบ":  `{"sku":"PEN-002","name":"x","qty":-1}`,
	}
	for name, body := range cases {
		if status, got := call(t, "POST", srv.URL+"/products", body); status != 400 {
			t.Errorf("%s: status %d (%s), want 400", name, status, got)
		}
	}
}

func TestListEmptyIsJSONArray(t *testing.T) {
	srv := newServer(t)
	status, body := call(t, "GET", srv.URL+"/products", "")
	var got []any
	if status != 200 || json.Unmarshal([]byte(body), &got) != nil || got == nil {
		t.Errorf("GET /products → %d %q, want 200 []", status, body)
	}
}

func TestHealthz(t *testing.T) {
	srv := newServer(t)
	if status, body := call(t, "GET", srv.URL+"/healthz", ""); status != 200 || !strings.Contains(body, "ok") {
		t.Errorf("healthz → %d %s", status, body)
	}
}

func TestWrongMethodIs405(t *testing.T) {
	srv := newServer(t)
	if status, _ := call(t, "DELETE", srv.URL+"/products/PEN-001", ""); status != http.StatusMethodNotAllowed {
		t.Errorf("DELETE → %d, want 405", status)
	}
}
