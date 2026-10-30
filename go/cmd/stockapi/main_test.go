package main

import (
	"context"
	"io"
	"net"
	"net/http"
	"path/filepath"
	"testing"
	"time"
)

// เปิด server จริงบน port ว่าง → /healthz ต้องได้ 200 → ยกเลิก ctx แล้ว run ต้องคืน nil (ปิดสะอาด)
func TestRunServesAndShutsDownCleanly(t *testing.T) {
	l, err := net.Listen("tcp", "127.0.0.1:0") // ขอ port ว่างจาก OS แล้วคืนทันที
	if err != nil {
		t.Fatal(err)
	}
	addr := l.Addr().String()
	l.Close()

	env := map[string]string{"ADDR": addr, "DB_PATH": filepath.Join(t.TempDir(), "t.db")}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- run(ctx, func(k string) string { return env[k] }, io.Discard) }()

	var resp *http.Response
	for i := 0; i < 50; i++ { // รอ server ขึ้น
		if resp, err = http.Get("http://" + addr + "/healthz"); err == nil {
			break
		}
		time.Sleep(20 * time.Millisecond)
	}
	if err != nil {
		t.Fatalf("server ไม่ขึ้น: %v", err)
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		t.Fatalf("healthz = %d, want 200", resp.StatusCode)
	}

	cancel()
	select {
	case err := <-done:
		if err != nil {
			t.Fatalf("run คืน error ตอนปิด: %v", err)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("ปิดไม่ลงภายใน 5 วินาที")
	}
}
