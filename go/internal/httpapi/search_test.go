package httpapi

import (
	"net/http"
	"net/url"
	"strings"
	"testing"
)

func TestSearchIgnoresSQLInjection(t *testing.T) {
	// Arrange — มีสินค้า 2 ตัว
	srv := newServer(t)
	call(t, "POST", srv.URL+"/products", `{"sku":"PEN-001","name":"pen","qty":5}`)
	call(t, "POST", srv.URL+"/products", `{"sku":"INK-001","name":"ink","qty":5}`)

	// Act — ค้นหาด้วย input อันตราย
	status, body := call(t, "GET", srv.URL+"/products/search?q="+url.QueryEscape("' OR 1=1 --"), "")

	// Assert — ไม่มีสินค้าชื่อแบบนั้น จึงต้องได้ list ว่าง
	if status != http.StatusOK || strings.TrimSpace(body) != "[]" {
		t.Errorf("search attack → %d %s, want 200 []", status, body)
	}
}
