// ตัวตรวจของโจทย์ · ห้ามแก้ไฟล์นี้ · รัน: go test -v ./...
package pool

import (
	"context"
	"errors"
	"runtime"
	"sync/atomic"
	"testing"
	"time"
)

func jobs(n int) []int {
	js := make([]int, n)
	for i := range js {
		js[i] = i
	}
	return js
}

// เกณฑ์ 1: พร้อมกันสูงสุดไม่เกิน limit และงานครบทุกชิ้น
func TestLimit(t *testing.T) {
	var now, peak, done int64
	err := Run(context.Background(), jobs(50), 5, func(ctx context.Context, j int) error {
		n := atomic.AddInt64(&now, 1)
		for p := atomic.LoadInt64(&peak); n > p && !atomic.CompareAndSwapInt64(&peak, p, n); p = atomic.LoadInt64(&peak) {
		}
		time.Sleep(10 * time.Millisecond)
		atomic.AddInt64(&now, -1)
		atomic.AddInt64(&done, 1)
		return nil
	})
	if err != nil {
		t.Fatalf("ไม่ควรมี error: %v", err)
	}
	if peak > 5 {
		t.Errorf("ทำพร้อมกันสูงสุด %d ตัว · ต้องไม่เกิน 5", peak)
	}
	if done != 50 {
		t.Errorf("ทำเสร็จ %d จาก 50 งาน", done)
	}
}

// เกณฑ์ 2: ยกเลิกแล้วหยุดเร็ว ไม่รับงานใหม่ และคืน context.Canceled
func TestCancel(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	var started int64
	go func() { time.Sleep(30 * time.Millisecond); cancel() }()
	begin := time.Now()
	err := Run(ctx, jobs(100), 5, func(ctx context.Context, j int) error {
		atomic.AddInt64(&started, 1)
		time.Sleep(20 * time.Millisecond)
		return nil
	})
	if !errors.Is(err, context.Canceled) {
		t.Errorf("ต้องคืน context.Canceled · ได้ %v", err)
	}
	if took := time.Since(begin); took > 200*time.Millisecond {
		t.Errorf("ยกเลิกแล้วยังทำต่ออีก %v · ควรหยุดภายใน 200ms", took)
	}
	if started > 20 {
		t.Errorf("เริ่มงานไปแล้ว %d ชิ้นหลังยกเลิก · ควรหยุดรับงานใหม่", started)
	}
}

// เกณฑ์ 3: จบแล้วไม่มี goroutine ค้าง
func TestNoLeak(t *testing.T) {
	before := runtime.NumGoroutine()
	ctx, cancel := context.WithCancel(context.Background())
	go func() { time.Sleep(10 * time.Millisecond); cancel() }()
	_ = Run(ctx, jobs(100), 5, func(ctx context.Context, j int) error { time.Sleep(5 * time.Millisecond); return nil })
	time.Sleep(50 * time.Millisecond)
	if after := runtime.NumGoroutine(); after > before+1 {
		t.Errorf("goroutine ก่อน %d หลัง %d · มีตัวค้าง", before, after)
	}
}
