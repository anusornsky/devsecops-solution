package worker

import (
	"context"
	"errors"
	"log/slog"
	"sync/atomic"
	"testing"
	"time"
)

// fakeProcessor มีงานค้าง jobs ชิ้น · นับว่าถูกเรียกกี่ครั้ง
// atomic.Int32 = ตัวนับที่หลาย goroutine อ่าน/เขียนพร้อมกันได้ปลอดภัย (C#: Interlocked.Increment)
type fakeProcessor struct {
	jobs     atomic.Int32
	calls    atomic.Int32
	recovers atomic.Int32
	err      error
}

func (f *fakeProcessor) RecoverStuck(context.Context) (int64, error) {
	f.recovers.Add(1)
	return 0, nil
}

func (f *fakeProcessor) ProcessNext(context.Context) (bool, error) {
	f.calls.Add(1)
	if f.err != nil {
		return false, f.err
	}
	if f.jobs.Load() == 0 {
		return false, nil
	}
	f.jobs.Add(-1)
	return true, nil
}

var quiet = slog.New(slog.DiscardHandler)

// startRun รัน worker ใน goroutine คืนฟังก์ชันสั่งปิดที่รอจนปิดเสร็จจริง
func startRun(p Processor) (stop func() bool) {
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() { Run(ctx, p, 10*time.Millisecond, quiet); close(done) }()
	return func() bool {
		cancel()
		select {
		case <-done:
			return true
		case <-time.After(2 * time.Second):
			return false
		}
	}
}

func TestRunDrainsQueueAndStopsOnCancel(t *testing.T) {
	p := &fakeProcessor{}
	p.jobs.Store(5)

	stop := startRun(p)
	time.Sleep(100 * time.Millisecond)

	if !stop() {
		t.Fatal("worker ไม่ปิดภายใน 2 วินาที")
	}
	if p.jobs.Load() != 0 {
		t.Errorf("งานเหลือ %d, want 0", p.jobs.Load())
	}
	if n := p.recovers.Load(); n != 0 {
		t.Errorf("recovers = %d, want 0 (ไม่มี error ก็ไม่ต้องคืนงานค้าง)", n)
	}
}

// ProcessNext พัง = order อาจค้าง processing (ย้ายสถานะไม่สำเร็จ) → รอบถัดไปต้องคืนงานค้างเข้าคิว ไม่ใช่รอรีสตาร์ท
func TestRunRecoversStuckAfterError(t *testing.T) {
	p := &fakeProcessor{err: errors.New("database is locked")}

	stop := startRun(p)
	time.Sleep(50 * time.Millisecond)
	stop()

	if p.recovers.Load() == 0 {
		t.Error("recovers = 0, want ≥ 1 หลัง ProcessNext พัง")
	}
}

// ระบบมีปัญหา → worker ต้องไม่ตาย และไม่วนเรียกรัวๆ (พักตาม interval)
func TestRunSurvivesErrorsAndBacksOff(t *testing.T) {
	p := &fakeProcessor{err: errors.New("database is locked")}

	stop := startRun(p)
	time.Sleep(100 * time.Millisecond)
	stop()

	// 100ms / พัก 10ms ≈ 10 ครั้ง · ถ้าไม่พักจะเป็นหลักแสน
	if n := p.calls.Load(); n < 2 || n > 30 {
		t.Errorf("calls = %d, want ประมาณ 10 (ลองซ้ำเป็นรอบ ไม่ใช่รัว)", n)
	}
}
